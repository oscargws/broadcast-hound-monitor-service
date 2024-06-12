using Supabase.Postgrest.Models;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;

namespace StreamMonitoringService
{
    [Table("streams")]
    public class Stream : BaseModel
    {
        // [Column("id")]
        [JsonProperty("id")]
        public Guid Id { get; set; }

        // [Column("url")]
        [JsonProperty("url")]
        public string Url { get; set; }

        // [Column("account_id")]
        [JsonProperty("account_id")]
        public Guid AccountId { get; set; }
    }
}