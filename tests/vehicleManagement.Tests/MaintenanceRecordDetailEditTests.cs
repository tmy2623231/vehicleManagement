using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.DAL.Common;
using vehicleManagement.DAL.Repository;
using Xunit;
using Models = vehicleManagement.DAL.Model;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// 维修记录的「详情 / 编辑 / 附件」三块能力：
    /// 详情必须带齐导航才能算出「类型」；编辑只改业务字段（身份快照与车辆归属不可动）；
    /// 附件为空时详情与拆分都不得报错。
    /// </summary>
    public class MaintenanceRecordDetailEditTests : IDisposable
    {
        private readonly TestDbFactory _factory = new();
        private readonly FakeWebHostEnv _env = new();
        private readonly StubCurrentUser _user;
        private readonly MaintenanceRecordService _service;

        public MaintenanceRecordDetailEditTests()
        {
            // 本组用例验证的是「记录维度的类型文案 + 编辑契约 + 附件拆分」，属全局管理视角：
            // 必须是 Admin（ScopeDepartmentId 恒为 null，可看全部部门），否则跨部门用例会被服务层隔离挡住。
            // 保留 DepartmentId/PersonnelId 是为了让 CreateAsync 写出的身份快照仍是「马存宝 / 信息安防技术室」。
            // 部门账号的可见范围另由 MaintenanceRecordScopeTests 覆盖。
            _user = new StubCurrentUser(new UserIdentity
            {
                IsAuthenticated = true,
                UserId = 100,
                UserName = "machi",
                Role = Models.Roles.Admin,
                DepartmentId = TestData.DeptA,
                PersonnelId = 10
            });
            _service = new MaintenanceRecordService(
                new MaintenanceRecordRepository(_factory),
                _factory,
                new FileUploadService(_env),
                _user,
                new AuditService(_factory, _user, NullLogger<AuditService>.Instance),
                NullLogger<MaintenanceRecordService>.Instance);
        }

        private static Models.VehicleMaintenanceRecord NewRecord(int vehicleId) => new()
        {
            VehicleId = vehicleId,
            MaintenanceDate = ChinaTime.Now,
            Description = "更换刹车片",
            Cost = 600m
        };

        // ===== 详情：三态文案 + 字段齐全 =====

        [Fact]
        public async Task GetDetailAsync_CrossDepartment_ShowsCrossDepartmentText()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            // DeptA 的人维修 DeptB 的车 → 跨部门
            var created = await _service.CreateAsync(NewRecord(2));

            var detail = await _service.GetDetailAsync(created.Id);

            Assert.NotNull(detail);
            // 导航齐全：车辆、车辆所属部门、维修人、维修部门
            Assert.Equal("京B12345", detail.Vehicle.LicensePlate);
            Assert.Equal("信息设备维护室", detail.Vehicle.Department.Name);
            Assert.Equal("马存宝", detail.MaintainerName);
            Assert.Equal("信息安防技术室", detail.MaintainerDepartmentName);
            Assert.Equal("跨部门维修", detail.MaintenanceKindText);
            Assert.Equal("badge-red", detail.MaintenanceKindBadgeClass);
        }

        [Fact]
        public async Task GetDetailAsync_SameDepartment_ShowsMaintenanceText()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            // DeptA 的人维修 DeptA 的车 → 同部门
            var created = await _service.CreateAsync(NewRecord(1));

            var detail = await _service.GetDetailAsync(created.Id);

            Assert.NotNull(detail);
            Assert.Equal("京A88601", detail.Vehicle.LicensePlate);
            Assert.Equal("信息安防技术室", detail.Vehicle.Department.Name);
            Assert.Equal("维修", detail.MaintenanceKindText);
            Assert.Equal("badge-green", detail.MaintenanceKindBadgeClass);
        }

        [Fact]
        public async Task GetDetailAsync_LegacyNullIdentity_ShowsDashWithoutThrowing()
        {
            // 老库补列后的历史记录：维修人 / 维修部门为 NULL，但车辆与部门仍然在
            using (var db = _factory.CreateDbContext())
            {
                await TestData.SeedBasicsAsync(db);
                db.VehicleMaintenanceRecords.Add(NewRecord(2));
                await db.SaveChangesAsync();
            }

            var id = (await _service.GetPagedAsync("", 1, 10)).Items.Single().Id;
            var detail = await _service.GetDetailAsync(id);

            Assert.NotNull(detail);
            Assert.Null(detail.MaintainerPersonnelId);
            Assert.Null(detail.MaintainerDepartmentId);
            Assert.Equal("未知", detail.MaintainerName);
            Assert.Equal("未知", detail.MaintainerDepartmentName);
            Assert.Equal("-", detail.MaintenanceKindText);
            Assert.Equal("", detail.MaintenanceKindBadgeClass);
            // 老数据的车辆信息仍要能显示
            Assert.Equal("京B12345", detail.Vehicle.LicensePlate);
        }

        [Fact]
        public async Task GetDetailAsync_MissingRecord_ReturnsNull()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            Assert.Null(await _service.GetDetailAsync(9999));
        }

        // ===== 编辑：只改业务字段，类型文案不随车辆状态变化 =====

        [Fact]
        public async Task UpdateAsync_KeepsKindText_WhenVehicleStatusChanges()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var created = await _service.CreateAsync(NewRecord(2));   // 跨部门记录

            // 车辆状态随时间变化（在修 → 报废），它不参与「类型」推导
            using (var db = _factory.CreateDbContext())
            {
                var vehicle = await db.Vehicles.FindAsync(2);
                vehicle.Status = Models.VehicleStatus.Retired;
                await db.SaveChangesAsync();
            }

            var patch = await _service.GetDetailAsync(created.Id);
            patch.Description = "更换刹车片 + 四轮定位";
            patch.Cost = 880m;
            patch.Remarks = "顺带做了动平衡";
            await _service.UpdateAsync(patch);

            var updated = await _service.GetDetailAsync(created.Id);
            Assert.Equal("更换刹车片 + 四轮定位", updated.Description);
            Assert.Equal(880m, updated.Cost);
            Assert.Equal("顺带做了动平衡", updated.Remarks);
            // 类型仍由记录维度的身份快照决定
            Assert.Equal("跨部门维修", updated.MaintenanceKindText);
        }

        [Fact]
        public async Task UpdateAsync_CannotMoveRecordToAnotherVehicle()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var created = await _service.CreateAsync(NewRecord(2));   // 跨部门记录

            // 前端伪造：把记录挪到同部门的车 1 上，试图把「跨部门维修」洗成「维修」
            var patch = await _service.GetDetailAsync(created.Id);
            patch.VehicleId = 1;
            await _service.UpdateAsync(patch);

            var updated = await _service.GetDetailAsync(created.Id);
            Assert.Equal(2, updated.VehicleId);
            Assert.Equal("跨部门维修", updated.MaintenanceKindText);
        }

        [Fact]
        public async Task UpdateAsync_CannotRewriteCreatedAt()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var created = await _service.CreateAsync(NewRecord(1));
            var originalCreatedAt = created.CreatedAt;

            var patch = await _service.GetDetailAsync(created.Id);
            patch.CreatedAt = new DateTime(2000, 1, 1);
            await _service.UpdateAsync(patch);

            var updated = await _service.GetDetailAsync(created.Id);
            Assert.Equal(originalCreatedAt, updated.CreatedAt);
        }

        [Fact]
        public async Task UpdateAsync_WritesAuditLogWithChangeSummary()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var created = await _service.CreateAsync(NewRecord(1));

            var patch = await _service.GetDetailAsync(created.Id);
            patch.Description = "更换雨刷";
            patch.Cost = 45m;
            await _service.UpdateAsync(patch);

            using var verify = _factory.CreateDbContext();
            var audit = verify.AuditLogs.Single(a => a.Action == "MaintenanceRecord.Update");

            Assert.Equal("VehicleMaintenanceRecord", audit.TargetType);
            Assert.Equal(created.Id, audit.TargetId);
            Assert.Equal("machi", audit.UserName);          // 操作人取当前登录者
            Assert.Contains("更换雨刷", audit.Detail);
            Assert.Contains("45.00", audit.Detail);
        }

        // ===== 附件 =====

        [Fact]
        public async Task GetAttachmentFiles_NullOrEmpty_ReturnsEmptyList()
        {
            // 无附件的新库记录：详情能打开、拆分返回空集合、不抛异常
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var created = await _service.CreateAsync(NewRecord(1));   // ImagePaths 未赋值 = null
            var detail = await _service.GetDetailAsync(created.Id);

            Assert.NotNull(detail);
            Assert.Null(detail.ImagePaths);
            Assert.Empty(_service.GetAttachmentFiles(detail));

            // 空串同样安全
            Assert.Empty(_service.GetAttachmentFiles(
                new Models.VehicleMaintenanceRecord { ImagePaths = "" }));
            Assert.Empty(_service.GetAttachmentFiles(
                new Models.VehicleMaintenanceRecord { ImagePaths = " ; ; " }));
        }

        [Fact]
        public async Task GetAttachmentFiles_SplitsSemicolonSeparatedPaths()
        {
            using (var db = _factory.CreateDbContext())
                await TestData.SeedBasicsAsync(db);

            var record = NewRecord(1);
            record.ImagePaths = "/Folder/维修记录/通用/通用/2026-11-11/a.jpg;/Folder/维修记录/通用/通用/2026-11-11/b.pdf";
            var created = await _service.CreateAsync(record);

            var detail = await _service.GetDetailAsync(created.Id);
            var files = _service.GetAttachmentFiles(detail);

            Assert.Equal(2, files.Count);
            Assert.True(_service.IsImageFile(files[0]));
            Assert.False(_service.IsImageFile(files[1]));
            Assert.False(_service.IsVideoFile(files[1]));   // 非图片非视频 → 页面走「新标签页打开」兜底
        }

        public void Dispose()
        {
            _factory.Dispose();
            _env.Dispose();
        }
    }
}
