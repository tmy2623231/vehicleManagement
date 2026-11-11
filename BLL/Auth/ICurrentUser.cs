using System.Threading.Tasks;

namespace vehicleManagement.BLL.Auth
{
    /// <summary>BLL 读取「当前是谁」的唯一通道（跨层边界，不碰 UI 类型）；异步读取以免同步上下文死锁。</summary>
    public interface ICurrentUser
    {
        /// <summary>读取当前登录者身份快照（同一 Circuit 内只读一次并缓存）。未登录返回 <see cref="UserIdentity.Anonymous"/>。</summary>
        Task<UserIdentity> GetAsync();
    }
}
