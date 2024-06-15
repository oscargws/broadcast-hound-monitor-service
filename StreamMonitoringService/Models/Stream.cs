using Supabase.Postgrest.Models;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;

namespace StreamMonitoringService
{
    [Table("streams")]
    public class Stream : BaseModel
    {
        [PrimaryKey("id")]
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("url")]
        [Column("url")]
        public string Url { get; set; }

        [JsonProperty("account_id")]
        [Column("account_id")]
        public Guid AccountId { get; set; }
        
        [JsonProperty("last_outage")]
        [Column("last_outage")]
        public DateTime LastOutage   { get; set; }
        
        [JsonProperty("last_online")]
        [Column("last_online")]
        public DateTime LastOnline   { get; set; }
        
        [JsonProperty("last_check")]
        [Column("last_check")]
        public DateTime LastCheck   { get; set; }
        
        [JsonProperty("status")]
        [Column("status")]
        public string Status   { get; set; }
        
    }
}