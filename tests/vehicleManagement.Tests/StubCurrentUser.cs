using System.Threading.Tasks;
using vehicleManagement.BLL.Auth;

namespace vehicleManagement.Tests
{
    /// <summary>测试用「当前登录者」桩：返回构造时给定的身份快照。</summary>
    internal sealed class StubCurrentUser : ICurrentUser
    {
        private readonly UserIdentity _identity;

        public StubCurrentUser(UserIdentity identity) => _identity = identity;

        public Task<UserIdentity> GetAsync() => Task.FromResult(_identity);

        public static StubCurrentUser Anonymous() => new(UserIdentity.Anonymous);

        public static StubCurrentUser Admin() => new(new UserIdentity
        {
            IsAuthenticated = true,
            UserId = 1,
            UserName = "admin",
            Role = vehicleManagement.DAL.Model.Roles.Admin,
            DepartmentId = null,
            PersonnelId = null
        });
    }
}
