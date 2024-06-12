using Supabase.Postgrest.Models;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;

namespace StreamMonitoringService
{
    [Table("checks")]
    public class Check : BaseModel
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("account_id")]
        public Guid AccountId { get; set; }
        
        [JsonProperty("stream")]
        public Guid Stream { get; set; }
        
        [JsonProperty("status")]
        public string Status { get; set; }
        
        [JsonProperty("completed")]
        public Boolean Completed { get; set; }
    }
}