namespace vehicleManagement.aiServer.BLL.Enums
{
    /// <summary>
    /// 用户意图枚举
    /// </summary>
    public enum UserIntent
    {
        /// <summary>查询车辆列表/详情</summary>
        VehicleQuery,
        /// <summary>查询在用车辆列表</summary>
        ActiveVehicleQuery,
        /// <summary>车辆统计/汇总</summary>
        VehicleStats,
        /// <summary>查询部门信息</summary>
        DepartmentQuery,
        /// <summary>查询人员信息</summary>
        PersonnelQuery,
        /// <summary>查询使用记录</summary>
        UsageRecordQuery,
        /// <summary>通用问答（不查数据库）</summary>
        GeneralChat
    }
}
