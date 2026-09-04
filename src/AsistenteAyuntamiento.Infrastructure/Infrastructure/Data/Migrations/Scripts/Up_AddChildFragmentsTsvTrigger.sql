CREATE FUNCTION ingestion.child_fragments_tsv_update() RETURNS trigger AS $$
BEGIN
  NEW."TsvContent" := to_tsvector('spanish', NEW."ChunkText");
  RETURN NEW;
END
$$ LANGUAGE plpgsql;

CREATE TRIGGER child_fragments_tsv_trigger
  BEFORE INSERT OR UPDATE OF "ChunkText"
  ON ingestion."ChildFragments"
  FOR EACH ROW
  EXECUTE FUNCTION ingestion.child_fragments_tsv_update();
