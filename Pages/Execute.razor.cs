#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using vehicleManagement.DAL.Model;
using vehicleManagement.DAL.Common;
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

    // ===== 注入的服务（配合 .razor 中的 @inject 指令） =====
    // ponytail: @inject 已在 .razor 中声明，partial class 可直接访问以下属性：
    // ExecService, VehicleService, MaintenanceService, LocationService, Nav, FileUploadService, JS

    // ===== UI 状态变量 =====
    private string activeTab = "usage";
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
    private List<Personnel> borrowPersonnel = [];
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
    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Token)) { loading = false; return; }

        var pageData = await ExecService.LoadPageDataAsync(Token);
        if (pageData != null)
        {
            vehicle = pageData.Vehicle;
            personnelList = pageData.PersonnelList;
            allWaypoints = pageData.Waypoints;
            allFinalLocations = pageData.FinalLocations;
            mileageStart = pageData.PreFilledMileageStart;
            borrowMileageStart = pageData.PreFilledMileageStart;
        }
        allDepartments = await VehicleService.GetAllDepartmentsAsync();
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
                UploadedFiles = uploadedFiles.Count != 0 ? uploadedFiles : null, Remarks = remarks
            };
            var (success, errorMsg) = await ExecService.SaveRecordAsync(request);
            if (!success) { errorMessage = errorMsg; return; }
            saved = true; _ = StartCountdown();
        }
        finally { saving = false; }
    }

    // ===== 倒计时自动关闭 =====
    private async Task StartCountdown()
    {
        while (countdown > 0) { await Task.Delay(1000); countdown--; StateHasChanged(); }
        try { await JS.InvokeVoidAsync("close"); } catch { }
    }

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
                Remarks = maintRemarks?.Trim(),
                ImagePaths = maintFiles.Count != 0 ? string.Join(";", maintFiles) : null,
                CreatedAt = ChinaTime.Now
            });
            saved = true; _ = StartCountdown();
        }
        catch (Exception ex) { maintError = $"保存失败：{ex.Message}"; }
        finally { maintSaving = false; }
    }

    // ===== 借车登记 =====
    private async Task OnBorrowDeptChanged(ChangeEventArgs e)
    {
        borrowDeptId = int.Parse(e.Value?.ToString() ?? "0");
        borrowPersonnelId = 0;
        borrowPersonnel = borrowDeptId > 0
            ? await VehicleService.GetPersonnelByDepartmentAsync(borrowDeptId) : [];
    }

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
            ? borrowPersonnel.FirstOrDefault(p => p.Id == borrowPersonnelId)?.Name ?? "未知人员"
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
        if (borrowPersonnelId == 0) { borrowError = "请选择使用人员"; return; }
        if (borrowWaypoints.Count == 0) { borrowError = "请选择至少一个途经路径点"; return; }
        if (borrowFinalLocId == 0) { borrowError = "请选择最终停车位置"; return; }
        borrowSaving = true;
        try
        {
            var request = new SaveUsageRecordRequest
            {
                VehicleId = vehicle!.Id, DepartmentId = borrowDeptId,
                PersonnelId = borrowPersonnelId, FinalLocationId = borrowFinalLocId,
                WaypointIds = borrowWaypoints.Select(s => s.Id).ToList(),
                MileageStart = borrowMileageStart, MileageEnd = borrowMileageEnd,
                UploadedFiles = borrowFiles.Count != 0 ? borrowFiles : null, Remarks = borrowRemarks
            };
            var (success, err) = await ExecService.SaveRecordAsync(request);
            if (!success) { borrowError = err; return; }
            await VehicleService.ToggleBorrowedAsync(vehicle!.Id, true);
            saved = true; _ = StartCountdown();
        }
        catch (Exception ex) { borrowError = $"保存失败：{ex.Message}"; }
        finally { borrowSaving = false; }
    }
}
