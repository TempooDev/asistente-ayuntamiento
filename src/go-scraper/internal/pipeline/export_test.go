package pipeline

// isRunning reports whether a job holds the lock.
func (o *Orchestrator) isRunning() bool {
	o.mu.Lock()
	defer o.mu.Unlock()
	return o.running
}
