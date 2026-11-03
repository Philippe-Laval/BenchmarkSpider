using System.Text.Json.Serialization;

namespace BenchmarkBird.Library.Models
{
    /// <summary>Une entrée du fichier BIRD (dev.json) : question, preuve et requête SQL de référence.</summary>
    public class BirdExample
    {
        [JsonPropertyName("question_id")]
        public int QuestionId { get; set; }

        [JsonPropertyName("db_id")]
        public string DbId { get; set; } = string.Empty;

        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        /// <summary>Connaissance externe aidant à répondre à la question (peut être vide).</summary>
        [JsonPropertyName("evidence")]
        public string Evidence { get; set; } = string.Empty;

        [JsonPropertyName("SQL")]
        public string Sql { get; set; } = string.Empty;

        /// <summary>simple, moderate ou challenging.</summary>
        [JsonPropertyName("difficulty")]
        public string Difficulty { get; set; } = string.Empty;
    }
}
