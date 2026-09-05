package messaging

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"

	amqp "github.com/rabbitmq/amqp091-go"
)

type DocumentMessage struct {
	Source     string `json:"source"`
	DocumentID string `json:"document_id"`
	BlobPath   string `json:"blob_path"`
}

type Publisher struct {
	conn    *amqp.Connection
	channel *amqp.Channel
	queueBaseline       amqp.Queue
	queueHierarchical   amqp.Queue
}

func NewPublisher() (*Publisher, error) {
	connStr := os.Getenv("ConnectionStrings__messaging")
	if connStr == "" {
		return nil, fmt.Errorf("no se encontró ConnectionStrings__messaging")
	}

	conn, err := amqp.Dial(connStr)
	if err != nil {
		return nil, fmt.Errorf("error conectando a RabbitMQ: %w", err)
	}

	ch, err := conn.Channel()
	if err != nil {
		conn.Close()
		return nil, fmt.Errorf("error abriendo canal de RabbitMQ: %w", err)
	}

	qBaseline, err := ch.QueueDeclare(
		"documents_to_process_baseline", // nombre de la cola
		true,                            // durable
		false,                           // delete when unused
		false,                           // exclusive
		false,                           // no-wait
		nil,                             // arguments
	)
	if err != nil {
		ch.Close()
		conn.Close()
		return nil, fmt.Errorf("error declarando la cola baseline: %w", err)
	}

	qHierarchical, err := ch.QueueDeclare(
		"documents_to_process_hierarchical", // nombre de la cola
		true,                                // durable
		false,                               // delete when unused
		false,                               // exclusive
		false,                               // no-wait
		nil,                                 // arguments
	)
	if err != nil {
		ch.Close()
		conn.Close()
		return nil, fmt.Errorf("error declarando la cola hierarchical: %w", err)
	}

	return &Publisher{
		conn:              conn,
		channel:           ch,
		queueBaseline:     qBaseline,
		queueHierarchical: qHierarchical,
	}, nil
}

func (p *Publisher) PublishDocument(ctx context.Context, msg DocumentMessage) error {
	body, err := json.Marshal(msg)
	if err != nil {
		return err
	}

	// Publish to baseline queue
	err = p.channel.PublishWithContext(ctx,
		"",                    // exchange
		p.queueBaseline.Name, // routing key (queue name)
		false,                 // mandatory
		false,                 // immediate
		amqp.Publishing{
			ContentType: "application/json",
			Body:        body,
		},
	)
	if err != nil {
		return fmt.Errorf("error publicando en RabbitMQ (baseline): %w", err)
	}

	// Publish to hierarchical queue
	err = p.channel.PublishWithContext(ctx,
		"",                        // exchange
		p.queueHierarchical.Name, // routing key (queue name)
		false,                     // mandatory
		false,                     // immediate
		amqp.Publishing{
			ContentType: "application/json",
			Body:        body,
		},
	)
	if err != nil {
		return fmt.Errorf("error publicando en RabbitMQ (hierarchical): %w", err)
	}

	log.Printf("Evento publicado en ambas colas de RabbitMQ: %s", string(body))
	return nil
}

func (p *Publisher) Close() {
	if p.channel != nil {
		p.channel.Close()
	}
	if p.conn != nil {
		p.conn.Close()
	}
}
