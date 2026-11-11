using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace vehicleManagement.DAL
{
    /// <summary>主库结构引导：给老库补表/补列/补索引（只用 IF NOT EXISTS，可重复执行）。</summary>
    public static class SchemaBootstrapper
    {
        private const string Ddl = """
            CREATE TABLE IF NOT EXISTS AppUsers (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName      TEXT    NOT NULL,
                PasswordHash  TEXT    NOT NULL,
                DisplayName   TEXT    NULL,
                Role          TEXT    NOT NULL DEFAULT 'User',
                DepartmentId  INTEGER NULL,
                PersonnelId   INTEGER NULL,
                IsActive      INTEGER NOT NULL DEFAULT 1,
                CreatedAt     TEXT    NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_AppUsers_UserName ON AppUsers (UserName);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_AppUsers_PersonnelId
                ON AppUsers (PersonnelId) WHERE PersonnelId IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_AppUsers_DepartmentId ON AppUsers (DepartmentId);

            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Action       TEXT    NOT NULL,
                TargetType   TEXT    NOT NULL,
                TargetId     INTEGER NOT NULL,
                Detail       TEXT    NULL,
                UserName     TEXT    NULL,
                DepartmentId INTEGER NULL,
                CreatedAt    TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAt ON AuditLogs (CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_AuditLogs_Action    ON AuditLogs (Action);

            -- 维修记录表：后加的功能，老库可能缺表；缺表会让下面的 CREATE INDEX 直接失败，
            -- 故先建表（形状同 EF，时间默认值按中国时间）。
            CREATE TABLE IF NOT EXISTS VehicleMaintenanceRecords (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                VehicleId       INTEGER NOT NULL,
                MaintenanceDate TEXT    NOT NULL,
                Description     TEXT    NOT NULL,
                Cost            REAL    NOT NULL DEFAULT 0,
                Remarks         TEXT    NULL,
                ImagePaths      TEXT    NULL,
                MaintainerPersonnelId  INTEGER NULL,
                MaintainerDepartmentId INTEGER NULL,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now', '+8 hours')),
                UpdatedAt       TEXT    NULL,
                FOREIGN KEY (VehicleId) REFERENCES Vehicles (Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_VehicleMaintenanceRecords_VehicleId ON VehicleMaintenanceRecords (VehicleId);

            -- 业务表索引：老库靠这里补，命名同 EF 约定；本串会被当格式串用，禁止出现花括号。
            CREATE INDEX IF NOT EXISTS IX_Personnel_DepartmentId_IsActive          ON Personnel (DepartmentId, IsActive);
            CREATE INDEX IF NOT EXISTS IX_Locations_Category_IsActive_SortOrder    ON Locations (Category, IsActive, SortOrder);
            CREATE INDEX IF NOT EXISTS IX_Vehicles_Status_LicensePlate             ON Vehicles (Status, LicensePlate);
            CREATE INDEX IF NOT EXISTS IX_VehicleUsageRecords_PersonnelId          ON VehicleUsageRecords (PersonnelId);
            CREATE INDEX IF NOT EXISTS IX_VehicleUsageRecords_FinalLocationId      ON VehicleUsageRecords (FinalLocationId);
            CREATE INDEX IF NOT EXISTS IX_VehicleUsageRecords_DepartmentId_CreatedAt ON VehicleUsageRecords (DepartmentId, CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_VehicleUsageRecords_VehicleId_CreatedAt    ON VehicleUsageRecords (VehicleId, CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_VehicleMaintenanceRecords_MaintenanceDate  ON VehicleMaintenanceRecords (MaintenanceDate);
            """;

        /// <summary>
        /// 幂等建表 + 补列。必须在 <c>EnsureCreatedAsync()</c> 之后、任何种子/登录逻辑之前调用。
        /// </summary>
        public static async Task ApplyAsync(VehicleDbContext db, ILogger log)
        {
            await db.Database.ExecuteSqlRawAsync(Ddl);

            // 老库缺列兜底
            await AddColumnIfMissingAsync(db, "AppUsers", "DisplayName", "TEXT NULL");
            await AddColumnIfMissingAsync(db, "AppUsers", "Role", "TEXT NOT NULL DEFAULT 'User'");
            await AddColumnIfMissingAsync(db, "AppUsers", "DepartmentId", "INTEGER NULL");
            await AddColumnIfMissingAsync(db, "AppUsers", "PersonnelId", "INTEGER NULL");
            await AddColumnIfMissingAsync(db, "AppUsers", "IsActive", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(db, "AppUsers", "CreatedAt", "TEXT NULL");

            await AddColumnIfMissingAsync(db, "AuditLogs", "UserName", "TEXT NULL");
            await AddColumnIfMissingAsync(db, "AuditLogs", "DepartmentId", "INTEGER NULL");

            // 使用记录：借车/使用是记录维度快照；老库无此概念，统一回退为「使用」
            await AddColumnIfMissingAsync(db, "VehicleUsageRecords", "IsBorrow", "INTEGER NOT NULL DEFAULT 0");

            // 维修记录：维修人/维修部门是后加的身份快照。ALTER TABLE 不接受非常量默认值，
            // 故只能是可空列，读取侧统一回退「未知」。
            await AddColumnIfMissingAsync(db, "VehicleMaintenanceRecords", "MaintainerPersonnelId", "INTEGER NULL");
            await AddColumnIfMissingAsync(db, "VehicleMaintenanceRecords", "MaintainerDepartmentId", "INTEGER NULL");

            // 索引必须晚于补列执行：列不存在时 CREATE INDEX 会直接失败
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS IX_VehicleMaintenanceRecords_MaintainerDepartmentId "
                + "ON VehicleMaintenanceRecords (MaintainerDepartmentId);");

            // 口径纠正：借车只是使用记录上的标记，不是车辆持久状态。
            // 存量 1 一次性归零；WHERE 过滤使重复执行影响 0 行（幂等），且不碰 VehicleUsageRecords.IsBorrow。
            var cleared = await db.Database.ExecuteSqlRawAsync(
                "UPDATE Vehicles SET IsBorrowed = 0 WHERE IsBorrowed <> 0;");
            if (cleared > 0)
                log.LogInformation("已把 {Count} 辆车的 IsBorrowed 存量值归零（借车只是记录标记，不是车辆状态）", cleared);

            log.LogInformation("schema 引导完成：AppUsers / AuditLogs、使用/维修记录新列与业务表索引已就绪");
        }

        private static async Task AddColumnIfMissingAsync(VehicleDbContext db, string table, string column, string definition)
        {
            var existing = await GetColumnsAsync(db, table);
            if (existing.Contains(column))
                return;

            // 表名/列名是本类常量，SQLite 的 DDL 又不支持标识符参数化，故直接拼接
            var sql = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition;
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task<HashSet<string>> GetColumnsAsync(VehicleDbContext db, string table)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await db.Database.OpenConnectionAsync();

            try
            {
                await using var cmd = connection.CreateCommand();
                // 表名来自本类常量，无外部输入
                cmd.CommandText = $"PRAGMA table_info({table});";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(1));
            }
            finally
            {
                if (shouldClose)
                    await db.Database.CloseConnectionAsync();
            }

            return columns;
        }
    }
}
