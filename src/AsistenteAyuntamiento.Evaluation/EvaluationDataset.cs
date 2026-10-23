using System.Security.Cryptography;
using System.Text.Json;

namespace AsistenteAyuntamiento.Evaluation;

/// <summary>One line of <c>questions.jsonl</c> (spec: rag-evaluation, Fixed Corpus and Question Set).</summary>
public sealed record EvaluationQuestion(string Id, string Question, string Reference, IReadOnlyList<string> ReferenceDocumentIds, string Category);

/// <summary>The question set and the corpus every variant ingested, with the hashes recorded in <c>run.json</c>.</summary>
public sealed record EvaluationDataset(IReadOnlyList<EvaluationQuestion> Questions, IReadOnlyList<string> CorpusDocumentIds, string DatasetHash, string CorpusHash)
{
    public const string QuestionsFile = "questions.jsonl";
    public const string CorpusFile = "corpus.txt";

    /// <summary>
    /// Reads <c>questions.jsonl</c> and <c>corpus.txt</c> (blob paths <c>json/{SOURCE}/{id}.json</c>; the id is the file
    /// name). Full validation is the scorer's <c>validate_dataset.py</c>; this only refuses what would break the run.
    /// </summary>
    public static EvaluationDataset Load(string directory)
    {
        var questionsPath = Path.Combine(directory, QuestionsFile);
        var corpusPath = Path.Combine(directory, CorpusFile);
        var questions = File.ReadLines(questionsPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, index) => JsonSerializer.Deserialize<EvaluationQuestion>(line, EvaluationJson.Options)
                ?? throw new InvalidDataException($"{QuestionsFile}, line {index + 1}: empty question."))
            .ToList();

        var duplicate = questions.GroupBy(q => q.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"{QuestionsFile}: question id '{duplicate.Key}' is repeated.");
        }

        var corpus = File.ReadLines(corpusPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Distinct()
            .ToList();

        return new EvaluationDataset(questions, corpus, Hash(questionsPath), Hash(corpusPath));
    }

    private static string Hash(string path) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
