# Semantic cache

`SqliteSemanticCache` stores question/answer pairs and finds similar previous
questions with vector search.

- Storage: SQLite (`cache_entries` table) via Microsoft.Data.Sqlite;
  `created_at` is Unix seconds, `hit_count` counts served hits.
- Vectors: 384-dim L2-normalized embeddings. When `vec0.dll` (sqlite-vec) sits
  next to the executable, the cache loads it and keeps vectors in a
  `query_vectors` vec0 virtual table declared with `distance_metric = cosine`
  (`similarity = 1 - distance`). Without the extension, a bounded in-memory
  FIFO list (500 entries) is scanned with cosine similarity.
- Lookup: a hit requires similarity >= `SqliteSemanticCache.SimilarityThreshold`
  (0.88); among candidates within 0.01 of the best, one whose
  `chart_context_hash` matches the current chart context is preferred.
- Maintenance: `PurgeOlderThanAsync` removes entries older than the given age
  that have fewer than 3 hits (popular entries survive); `GetRecentAsync` lists
  the newest entries first.
- Chart fingerprints come from `ChartContextHash.Compute` (normalized OCR text
  plus pattern labels bucketed to 0.1 confidence).
