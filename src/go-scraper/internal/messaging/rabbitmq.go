package messaging

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"os"
	"sync"
	"time"

	amqp "github.com/rabbitmq/amqp091-go"
)

// Topology shared with the .NET services (RabbitMqTopology.cs, docs/messaging-contract.md).
// Queue arguments must be identical on both sides, including integer types (int32).
const (
	BaselineQueue     = "documents_to_process_baseline"
	HierarchicalQueue = "documents_to_process_hierarchical"
	retryDelayMs      = int32(30000)
	confirmTimeout    = 10 * time.Second
)

func retryQueue(queue string) string      { return queue + ".retry" }
func deadLetterQueue(queue string) string { return queue + ".dlq" }

type DocumentMessage struct {
	Source     string `json:"source"`
	DocumentID string `json:"document_id"`
	BlobPath   string `json:"blob_path"`
}

// Publisher publishes persistent messages with publisher confirms. It is safe for concurrent use:
// the channel is guarded by a mutex, and the connection is re-established when it is closed.
type Publisher struct {
	url string

	mu      sync.Mutex
	conn    *amqp.Connection
	channel *amqp.Channel
}

// NewPublisher validates the configuration and tries to connect. A failed first connection is not fatal:
// the publisher reconnects on the next publish.
func NewPublisher() (*Publisher, error) {
	url := os.Getenv("ConnectionStrings__messaging")
	if url == "" {
		return nil, errors.New("no se encontró ConnectionStrings__messaging")
	}

	p := &Publisher{url: url}
	p.mu.Lock()
	defer p.mu.Unlock()
	if err := p.connectLocked(); err != nil {
		log.Printf("Aviso: RabbitMQ no disponible al arrancar (se reintentará al publicar): %v", err)
	}
	return p, nil
}

func (p *Publisher) connectLocked() error {
	p.closeLocked()

	conn, err := amqp.Dial(p.url)
	if err != nil {
		return fmt.Errorf("conectando a RabbitMQ: %w", err)
	}

	ch, err := conn.Channel()
	if err != nil {
		conn.Close()
		return fmt.Errorf("abriendo canal de RabbitMQ: %w", err)
	}

	if err := ch.Confirm(false); err != nil {
		ch.Close()
		conn.Close()
		return fmt.Errorf("activando publisher confirms: %w", err)
	}

	for _, queue := range []string{BaselineQueue, HierarchicalQueue} {
		if err := declareWorkQueue(ch, queue); err != nil {
			ch.Close()
			conn.Close()
			return err
		}
	}

	p.conn = conn
	p.channel = ch
	return nil
}

// declareWorkQueue declares Q, Q.retry and Q.dlq exactly like RabbitMqTopology.DeclareWorkQueueAsync.
func declareWorkQueue(ch *amqp.Channel, queue string) error {
	if _, err := ch.QueueDeclare(deadLetterQueue(queue), true, false, false, false, nil); err != nil {
		return fmt.Errorf("declarando %s: %w", deadLetterQueue(queue), err)
	}

	if _, err := ch.QueueDeclare(retryQueue(queue), true, false, false, false, amqp.Table{
		"x-message-ttl":             retryDelayMs,
		"x-dead-letter-exchange":    "",
		"x-dead-letter-routing-key": queue,
	}); err != nil {
		return fmt.Errorf("declarando %s: %w", retryQueue(queue), err)
	}

	if _, err := ch.QueueDeclare(queue, true, false, false, false, amqp.Table{
		"x-dead-letter-exchange":    "",
		"x-dead-letter-routing-key": deadLetterQueue(queue),
	}); err != nil {
		return fmt.Errorf("declarando %s: %w", queue, err)
	}

	return nil
}

// PublishDocument publishes the message to both ingestion queues and waits for the broker confirms.
// On a closed connection it reconnects once and retries.
func (p *Publisher) PublishDocument(ctx context.Context, msg DocumentMessage) error {
	body, err := json.Marshal(msg)
	if err != nil {
		return err
	}

	p.mu.Lock()
	defer p.mu.Unlock()

	var lastErr error
	for attempt := 0; attempt < 2; attempt++ {
		if p.channel == nil || p.channel.IsClosed() || p.conn == nil || p.conn.IsClosed() {
			if err := p.connectLocked(); err != nil {
				lastErr = err
				continue
			}
		}

		lastErr = p.publishBothLocked(ctx, msg.DocumentID, body)
		if lastErr == nil {
			return nil
		}
		if !errors.Is(lastErr, amqp.ErrClosed) {
			return lastErr
		}
		p.closeLocked()
	}

	return lastErr
}

func (p *Publisher) publishBothLocked(ctx context.Context, documentID string, body []byte) error {
	for _, queue := range []string{BaselineQueue, HierarchicalQueue} {
		confirmation, err := p.channel.PublishWithDeferredConfirmWithContext(ctx, "", queue, true, false, amqp.Publishing{
			ContentType:  "application/json",
			DeliveryMode: amqp.Persistent,
			MessageId:    documentID,
			Timestamp:    time.Now().UTC(),
			Body:         body,
		})
		if err != nil {
			return fmt.Errorf("publicando en %s: %w", queue, err)
		}

		confirmCtx, cancel := context.WithTimeout(ctx, confirmTimeout)
		acked, err := confirmation.WaitContext(confirmCtx)
		cancel()
		if err != nil {
			return fmt.Errorf("esperando confirmación de %s: %w", queue, err)
		}
		if !acked {
			return fmt.Errorf("el broker rechazó el mensaje en %s", queue)
		}
	}
	return nil
}

func (p *Publisher) closeLocked() {
	if p.channel != nil {
		p.channel.Close()
		p.channel = nil
	}
	if p.conn != nil {
		p.conn.Close()
		p.conn = nil
	}
}

func (p *Publisher) Close() {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.closeLocked()
}
