using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicleManagement.aiServer.DAL.Model
{
    /// <summary>
    /// AI 语义记忆：跨会话存储关键信息
    /// </summary>
    public class ChatMemory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 记忆键/标识
        /// </summary>
        [Required]
        [MaxLength(256)]
        public string Key { get; set; }

        /// <summary>
        /// 记忆内容
        /// </summary>
        [Required]
        public string Content { get; set; }

        /// <summary>
        /// 记忆分类：vehicle / personnel / department / location / preference / general
        /// </summary>
        [MaxLength(50)]
        public string Category { get; set; }

        /// <summary>
        /// 来源会话ID（可选）
        /// </summary>
        public int? SessionId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 导航属性：来源会话
        /// </summary>
        [ForeignKey(nameof(SessionId))]
        public ChatSession Session { get; set; }
    }
}
