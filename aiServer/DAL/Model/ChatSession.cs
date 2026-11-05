using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicleManagement.aiServer.DAL.Model
{
    /// <summary>
    /// AI 对话会话
    /// </summary>
    public class ChatSession
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 会话标题（可自动根据首条消息生成）
        /// </summary>
        [MaxLength(200)]
        public string Title { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最后活跃时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 导航属性：会话中的消息列表
        /// </summary>
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}
