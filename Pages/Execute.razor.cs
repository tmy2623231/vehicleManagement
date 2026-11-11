#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using vehicleManagement.DAL.Model;
using vehicleManagement.DAL.Common;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Execute;
using vehicleManagement.BLL.Vehicle;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.BLL.Department;
using vehicleManagement.BLL.Personnel;
using vehicleManagement.BLL.Location;

namespace vehicleManagement.Pages;

/// <summary>车辆使用登记页面 — 代码后置逻辑</summary>
public partial class Execute
{
    // ===== 路由参数（配合 @page "/execute/{Token}"） =====
    [Parameter] public string Token { get; set; } = "";

    // 注入的服务由 .razor 的 @inject 声明，本类直接访问

    // ===== 登录身份（服务端定归属，前端传值一律不算数） =====
    private bool isBorrow;
    private string currentUserName = "";
    private string currentDepartmentName = "";

    /// <summary>「姓名（部门）」只读展示文案，借车/使用/维修三处共用</summary>
    private string CurrentUserDisplay => string.IsNullOrEmpty(currentDepartmentName)
        ? currentUserName
        : $"{currentUserName}（{currentDepartmentName}）";

    // ===== UI 状态变量 =====
    private string activeTab = "main";
    private bool loading = true;
    private bool saving;
    private bool uploading;
    private bool saved;
    private int countdown = 3;
    private Vehicle? vehicle;
    private List<Personnel> personnelList = [];
    private List<Department> allDepartments = [];
    private List<Location> allWaypoints = [];
    private List<Location> allFinalLocations = [];
    private List<Location> selectedWaypoints = [];
    private List<string> uploadedFiles = [];

    // 车辆使用
    private int selectedPersonnelId;
    private int selectedFinalLocationId;
    private double? mileageStart;
    private double? mileageEnd;
    private string remarks = "";
    private string errorMessage = "";

    // 车辆维修
    private DateTime maintDate = ChinaTime.Now;
    private string maintDesc = "";
    private decimal maintCost;
    private string maintRemarks = "";
    private List<string> maintFiles = [];
    private bool maintUploading;
    private bool maintSaving;
    private string maintError = "";

    // 借车登记
    private int borrowDeptId;
    private int borrowPersonnelId;
    private List<Location> borrowWaypoints = [];
    private int borrowFinalLocId;
    private double? borrowMileageStart;
    private double? borrowMileageEnd;
    private string borrowRemarks = "";
    private List<string> borrowFiles = [];
    private bool borrowUploading;
    private bool borrowSaving;
    private string borrowError = "";

    // ===== 生命周期 =====
    private string? loadedToken;

    /// <summary>Token 变化（换一辆车扫码）必须按新车辆重算预填，不能沿用上一辆车的旧值</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (loadedToken == Token) return;
        loadedToken = Token;
        await LoadPageAsync();
    }

    /// <summary>换车 / 重新登记：先清空上一辆车留下的全部表单状态，再按新车辆加载</summary>
    private void ResetFormState()
    {
        saved = false;
        countdown = 3;
        activeTab = "main";
        isBorrow = false;
        vehicle = null;
        currentUserName = "";
        currentDepartmentName = "";

        selectedFinalLocationId = 0;
        selectedWaypoints = [];
        mileageStart = null;
        mileageEnd = null;
        remarks = "";
        uploadedFiles = [];

        maintDate = ChinaTime.Now;
        maintDesc = "";
        maintCost = 0;
        maintRemarks = "";
        maintFiles = [];

        borrowDeptId = 0;
        borrowPersonnelId = 0;
        borrowWaypoints = [];
        borrowFinalLocId = 0;
        borrowMileageStart = null;
        borrowMileageEnd = null;
        borrowRemarks = "";
        borrowFiles = [];

        ClearErrors();
    }

    private async Task LoadPageAsync()
    {
        ResetFormState();

        if (string.IsNullOrEmpty(Token)) { loading = false; return; }

        loading = true;

        // 先取身份：部门异同决定「使用 / 借车」表单形态，使用人员固定为本人
        var me = await CurrentUser.GetAsync();

        var pageData = await ExecService.LoadPageDataAsync(Token);
        if (pageData != null)
        {
            vehicle = pageData.Vehicle;
            isBorrow = pageData.IsBorrow;
            personnelList = pageData.PersonnelList;
            allWaypoints = pageData.Waypoints;
            allFinalLocations = pageData.FinalLocations;
            // 使用 / 借车两个分支共用这一份预填值；只是默认值，输入框仍可手工改写
            mileageStart = pageData.PreFilledMileageStart;
            borrowMileageStart = pageData.PreFilledMileageStart;

            // 使用人员恒为登录本人，不给改选入口
            selectedPersonnelId = me.PersonnelId ?? 0;
            borrowPersonnelId = selectedPersonnelId;
            borrowDeptId = me.DepartmentId ?? pageData.Vehicle.DepartmentId;

            currentUserName = personnelList.FirstOrDefault(p => p.Id == selectedPersonnelId)?.Name
                              ?? me.UserName;
        }

        allDepartments = await VehicleService.GetAllDepartmentsAsync();
        currentDepartmentName = allDepartments.FirstOrDefault(d => d.Id == me.DepartmentId)?.Name
                                ?? vehicle?.Department?.Name
                                ?? "-";

        // 使用人员不属于车辆所属部门时，人员列表可能为空，名字回退到登录名
        if (string.IsNullOrEmpty(currentUserName))
            currentUserName = me.UserName;

        loading = false;
    }

    // ===== 工具方法 =====
    private void ClearErrors()
    {
        errorMessage = "";
        maintError = "";
        borrowError = "";
    }

    // ===== 路径点操作 =====
    private void ToggleWaypoint(Location waypoint)
    {
        var existing = selectedWaypoints.FirstOrDefault(s => s.Id == waypoint.Id);
        if (existing != null) selectedWaypoints.Remove(existing);
        else selectedWaypoints.Add(waypoint);
    }

    private void RemoveWaypoint(int id) => selectedWaypoints.RemoveAll(s => s.Id == id);

    // ===== 车辆使用 =====
    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        uploading = true; errorMessage = "";
        var deptName = vehicle?.Department?.Name ?? "未知部门";
        var plate = vehicle?.LicensePlate ?? "未知车牌";
        var personName = selectedPersonnelId > 0
            ? personnelList.FirstOrDefault(p => p.Id == selectedPersonnelId)?.Name ?? "未知人员"
            : "未知人员";
        foreach (var file in e.GetMultipleFiles(15))
        {
            try { uploadedFiles.Add(await FileUploadService.UploadFileAsync(file, deptName, plate, personName)); }
            catch (Exception ex) { errorMessage = $"文件上传失败：{ex.Message}"; }
        }
        uploading = false;
    }

    private async Task SaveRecord()
    {
        errorMessage = ""; saving = true;
        try
        {
            var request = new SaveUsageRecordRequest
            {
                VehicleId = vehicle!.Id, DepartmentId = vehicle.DepartmentId,
                PersonnelId = selectedPersonnelId, FinalLocationId = selectedFinalLocationId,
                WaypointIds = selectedWaypoints.Select(s => s.Id).ToList(),
                MileageStart = mileageStart, MileageEnd = mileageEnd,
                // null! = 有意置空（库里存 NULL），不是漏赋值
                UploadedFiles = uploadedFiles.Count != 0 ? uploadedFiles : null!, Remarks = remarks
            };
            var (success, errorMsg) = await ExecService.SaveRecordAsync(request);
            if (!success) { errorMessage = errorMsg; return; }
            saved = true;
            // 有意 fire-and-forget：倒计时自身完吞异常，不想阻断已成功的保存流程
            _ = StartCountdown();
        }
        finally { saving = false; }
    }

    // ===== 倒计时自动关闭 =====
    private readonly CancellationTokenSource _countdownCts = new();

    private async Task StartCountdown()
    {
        try
        {
            while (countdown > 0)
            {
                await Task.Delay(1000, _countdownCts.Token);
                countdown--;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) { return; }   // 电路已断，不再碰 UI

        try { await JS.InvokeVoidAsync("close"); }
        catch (Exception ex) { Logger.LogDebug(ex, "关闭窗口失败（多为浏览器拦截）"); }
    }

    // 只 Cancel 不 Dispose，避免与在途 Task.Delay 竞争
    public void Dispose() => _countdownCts.Cancel();

    // ===== 车辆维修 =====
    private async Task OnMaintFileSelected(InputFileChangeEventArgs e)
    {
        maintUploading = true; maintError = "";
        var deptName = vehicle?.Department?.Name ?? "未知部门";
        var plate = vehicle?.LicensePlate ?? "未知车牌";
        foreach (var file in e.GetMultipleFiles(10))
        {
            try { maintFiles.Add(await FileUploadService.UploadFileAsync(file, deptName, plate, "维修")); }
            catch (Exception ex) { maintError = $"上传失败：{ex.Message}"; }
        }
        maintUploading = false;
    }

    private async Task SaveMaintenance()
    {
        maintError = "";
        if (string.IsNullOrWhiteSpace(maintDesc)) { maintError = "请输入维修项目"; return; }
        maintSaving = true;
        try
        {
            await MaintenanceService.CreateAsync(new VehicleMaintenanceRecord
            {
                VehicleId = vehicle!.Id, MaintenanceDate = maintDate,
                Description = maintDesc.Trim(), Cost = maintCost,
                Remarks = maintRemarks?.Trim()!,
                ImagePaths = maintFiles.Count != 0 ? string.Join(";", maintFiles) : null!,
                CreatedAt = ChinaTime.Now
            });
            saved = true;
            // 有意 fire-and-forget：倒计时自身完吞异常，不想阻断已成功的保存流程
            _ = StartCountdown();
        }
        catch (Exception ex) { maintError = "保存失败，请稍后重试或联系管理员"; Logger.LogError(ex, "保存维修记录失败 VehicleId={Id}", vehicle?.Id); }
        finally { maintSaving = false; }
    }

    // ===== 借车登记 =====
    private void ToggleBorrowWaypoint(Location wp)
    {
        var existing = borrowWaypoints.FirstOrDefault(s => s.Id == wp.Id);
        if (existing != null) borrowWaypoints.Remove(existing);
        else borrowWaypoints.Add(wp);
    }

    private void RemoveBorrowWaypoint(int id) => borrowWaypoints.RemoveAll(s => s.Id == id);

    private async Task OnBorrowFileSelected(InputFileChangeEventArgs e)
    {
        borrowUploading = true; borrowError = "";
        var deptName = vehicle?.Department?.Name ?? "未知部门";
        var plate = vehicle?.LicensePlate ?? "未知车牌";
        var personName = borrowPersonnelId > 0
            ? borrowPersonnelId == selectedPersonnelId ? currentUserName : "未知人员"
            : "未知人员";
        foreach (var file in e.GetMultipleFiles(15))
        {
            try { borrowFiles.Add(await FileUploadService.UploadFileAsync(file, deptName, plate, personName)); }
            catch (Exception ex) { borrowError = $"上传失败：{ex.Message}"; }
        }
        borrowUploading = false;
    }

    private async Task SaveBorrow()
    {
        borrowError = "";
        if (borrowWaypoints.Count == 0) { borrowError = "请选择至少一个途经路径点"; return; }
        if (borrowFinalLocId == 0) { borrowError = "请选择最终停车位置"; return; }
        borrowSaving = true;
        try
        {
            var request = new SaveUsageRecordRequest
            {
                VehicleId = vehicle!.Id, DepartmentId = borrowDeptId,
                // 使用人员恒为登录本人（服务端也会再覆盖一次，这里传对是为了上传文件夹名正确）
                PersonnelId = borrowPersonnelId,
                FinalLocationId = borrowFinalLocId,
                WaypointIds = borrowWaypoints.Select(s => s.Id).ToList(),
                MileageStart = borrowMileageStart, MileageEnd = borrowMileageEnd,
                UploadedFiles = borrowFiles.Count != 0 ? borrowFiles : null!, Remarks = borrowRemarks
            };
            var (success, err) = await ExecService.SaveRecordAsync(request);
            if (!success) { borrowError = err; return; }
            saved = true;
            // 有意 fire-and-forget：倒计时自身完吞异常，不想阻断已成功的保存流程
            _ = StartCountdown();
        }
        catch (Exception ex) { borrowError = "保存失败，请稍后重试或联系管理员"; Logger.LogError(ex, "保存借车记录失败 VehicleId={Id}", vehicle?.Id); }
        finally { borrowSaving = false; }
    }
}
