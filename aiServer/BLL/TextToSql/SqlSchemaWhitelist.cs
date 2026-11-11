using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#nullable enable

namespace vehicleManagement.aiServer.BLL.TextToSql
{
    /// <summary>
    /// Text-to-SQL 副路径的唯一白名单来源：只放业务表，账号/审计表永不进白名单。
    /// 表名、列名用 SQLite 里的真实标识符（EF Core 用属性名原样落库，SQLite 标识符大小写不敏感）。
    /// </summary>
    public static class SqlSchemaWhitelist
    {
        public static readonly IReadOnlyDictionary<string, string[]> Tables =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Departments"] = new[] { "Id", "Name", "Description", "CreatedAt", "UpdatedAt" },
                ["Personnel"] = new[] { "Id", "DepartmentId", "Name", "Phone", "IsActive", "CreatedAt", "UpdatedAt" },
                ["Vehicles"] = new[]
                {
                    "Id", "DepartmentId", "LicensePlate", "Brand", "Model", "Year", "Color",
                    "Status", "QrCodeToken", "ImagePath", "Remarks", "IsBorrowed", "CreatedAt", "UpdatedAt"
                },
                ["Locations"] = new[] { "Id", "Name", "Description", "Category", "SortOrder", "IsActive", "CreatedAt" },
                ["VehicleUsageRecords"] = new[]
                {
                    "Id", "VehicleId", "DepartmentId", "PersonnelId", "IsBorrow", "FinalLocationId",
                    "PurposeSequence", "MileageStart", "MileageEnd", "ImagePaths", "VideoPaths", "Remarks", "CreatedAt"
                },
                ["VehicleUsageRecordPurposes"] = new[] { "Id", "UsageRecordId", "PurposeId", "SequenceOrder" },
                ["VehicleMaintenanceRecords"] = new[]
                {
                    "Id", "VehicleId", "MaintenanceDate", "Description", "Cost", "Remarks", "ImagePaths",
                    "CreatedAt", "UpdatedAt", "MaintainerPersonnelId", "MaintainerDepartmentId"
                }
            };

        /// <summary>即使写成别的形式也必须被拒绝的标识符（账号/审计/元数据）。</summary>
        public static readonly IReadOnlyCollection<string> ForbiddenIdentifiers = new[]
        {
            "AppUsers", "AuditLogs", "sqlite_master", "sqlite_schema", "sqlite_temp_master", "sqlite_sequence"
        };

        private static readonly HashSet<string> _tableNames =
            new(Tables.Keys, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _columnNames =
            new(Tables.Values.SelectMany(c => c), StringComparer.OrdinalIgnoreCase);

        public static bool IsTableAllowed(string name) => name.Length > 0 && _tableNames.Contains(name);

        public static bool IsColumnAllowed(string name) => name.Length > 0 && _columnNames.Contains(name);

        /// <summary>供提示词使用的表结构说明（只有白名单内的库表）。</summary>
        public static string DescribeForPrompt()
        {
            var sb = new StringBuilder();
            foreach (var (table, columns) in Tables)
                sb.AppendLine($"- {table} ({string.Join(", ", columns)})");
            return sb.ToString().TrimEnd();
        }
    }
}
