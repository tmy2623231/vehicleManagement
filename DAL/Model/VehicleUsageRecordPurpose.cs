using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicleManagement.DAL.Model
{
    public class VehicleUsageRecordPurpose
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UsageRecordId { get; set; }

        [Required]
        public int PurposeId { get; set; }

        [Required]
        public int SequenceOrder { get; set; }

        [ForeignKey("UsageRecordId")]
        public VehicleUsageRecord UsageRecord { get; set; }

        [ForeignKey("PurposeId")]
        public Location Purpose { get; set; }
    }
}
