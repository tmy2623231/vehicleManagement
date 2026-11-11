using vehicleManagement.aiServer.BLL.TextToSql;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// Text-to-SQL 的安全边界：越界表名/列名、多语句、非 SELECT、注释、参数占位符一律拒绝；
    /// 合法查询强制带 LIMIT（≤200）。
    /// </summary>
    public class TextToSqlValidatorTests
    {
        [Fact]
        public void ValidSelect_WithoutLimit_GetsForcedLimit()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles");

            Assert.True(result.IsValid);
            Assert.EndsWith($" LIMIT {TextToSqlValidator.MaxRows}", result.Sql!);
        }

        [Fact]
        public void ValidSelect_WithSmallLimit_IsKept()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles LIMIT 10");

            Assert.True(result.IsValid);
            Assert.Equal("SELECT LicensePlate FROM Vehicles LIMIT 10", result.Sql);
        }

        [Fact]
        public void HugeLimit_IsClampedToMaxRows()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles LIMIT 5000");

            Assert.True(result.IsValid);
            Assert.Contains($"LIMIT {TextToSqlValidator.MaxRows}", result.Sql);
            Assert.DoesNotContain("5000", result.Sql);
        }

        [Fact]
        public void MarkdownFencedSql_IsUnwrapped()
        {
            var result = TextToSqlValidator.Validate("```sql\nSELECT Name FROM Departments LIMIT 5;\n```");

            Assert.True(result.IsValid);
            Assert.Equal("SELECT Name FROM Departments LIMIT 5", result.Sql);
        }

        [Fact]
        public void AliasesAndJoins_AreAllowed()
        {
            var result = TextToSqlValidator.Validate(
                "SELECT v.LicensePlate, d.Name FROM Vehicles v JOIN Departments d ON d.Id = v.DepartmentId LIMIT 5");

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AggregateAlias_IsAllowed()
        {
            var result = TextToSqlValidator.Validate(
                "SELECT COUNT(*) AS total FROM VehicleUsageRecords LIMIT 1");

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("DELETE FROM Vehicles")]
        [InlineData("UPDATE Vehicles SET Remarks = 'x'")]
        [InlineData("DROP TABLE Vehicles")]
        [InlineData("PRAGMA table_info(Vehicles)")]
        [InlineData("ATTACH DATABASE 'evil.db' AS evil")]
        [InlineData("INSERT INTO Vehicles (LicensePlate) VALUES ('x')")]
        public void NonSelectStatements_AreRejected(string sql)
        {
            var result = TextToSqlValidator.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Null(result.Sql);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public void MultipleStatements_AreRejected()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles; DROP TABLE Vehicles");

            Assert.False(result.IsValid);
            Assert.Contains("单条", result.Reason!);
        }

        [Fact]
        public void Comments_AreRejected()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles -- 偷懒");

            Assert.False(result.IsValid);
            Assert.Contains("注释", result.Reason!);
        }

        [Fact]
        public void ParameterPlaceholder_IsRejected()
        {
            var result = TextToSqlValidator.Validate("SELECT LicensePlate FROM Vehicles WHERE Id = ?");

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData("SELECT * FROM AppUsers")]
        [InlineData("SELECT * FROM AuditLogs")]
        [InlineData("SELECT * FROM sqlite_master")]
        [InlineData("SELECT * FROM SecretTable")]
        public void OutOfWhitelistTables_AreRejected(string sql)
        {
            var result = TextToSqlValidator.Validate(sql);

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData("SELECT Password FROM Vehicles")]
        [InlineData("SELECT Vehicles.PasswordHash FROM Vehicles")]
        [InlineData("SELECT Name FROM SecretTable")]
        public void OutOfWhitelistColumns_AreRejected(string sql)
        {
            var result = TextToSqlValidator.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains("白名单", result.Reason!);
        }

        [Fact]
        public void EmptySql_IsRejected()
        {
            Assert.False(TextToSqlValidator.Validate(null).IsValid);
            Assert.False(TextToSqlValidator.Validate("   ").IsValid);
        }

        [Fact]
        public void CteQuery_IsAllowed()
        {
            var result = TextToSqlValidator.Validate(
                "WITH recent AS (SELECT Id FROM VehicleUsageRecords LIMIT 10) SELECT COUNT(*) AS c FROM recent");

            Assert.True(result.IsValid);
        }
    }
}
