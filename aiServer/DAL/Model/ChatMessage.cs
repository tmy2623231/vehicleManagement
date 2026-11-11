using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicleManagement.aiServer.DAL.Model
{
    /// <summary>
    /// AI 聊天消息
    /// </summary>
    public class ChatMessage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 所属会话ID
        /// </summary>
        public int SessionId { get; set; }

        /// <summary>
        /// 消息角色：user / assistant / system
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Role { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        [Required]
        public string Content { get; set; }

        /// <summary>
        /// AI 思考过程内容（仅 assistant 角色使用）
        /// </summary>
        // 可空：库里该列是 TEXT NULL
        public string? ThinkingContent { get; set; }

        /// <summary>
        /// 消息时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 导航属性：所属会话
        /// </summary>
        [ForeignKey(nameof(SessionId))]
        public ChatSession Session { get; set; }
    }
}
