using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.context;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// [DbFact]: a test that needs a real PostgreSQL. It runs only when SMSS_TEST_PG points at a SCRATCH server, e.g.
    ///   SMSS_TEST_PG="Host=127.0.0.1;Port=55432;Username=scratch"   and   SMSS_TEST_TEMPLATE=smss_tpl  (a restore of the schema + data, already migrated with
    ///   Docs/smss-fee-tax-types-RUN-ME.sql, never connected to). Each test gets its own fresh database cloned from the template, so tests cannot affect each other.
    /// Without the variable the test is skipped (it shows as skipped, not as passed). The real server (port 5432) is refused outright.
    /// </summary>
    public sealed class DbFactAttribute : FactAttribute
    {
        public DbFactAttribute()
        {
            if (TaxDb.Server == null) Skip = "Set SMSS_TEST_PG (a scratch PostgreSQL) and SMSS_TEST_TEMPLATE to run the database tests.";
        }
    }

    /// <summary>One throwaway database for one test, with the services wired the way Program.cs wires them (a fresh DbContext per call, like a request).</summary>
    public sealed class TaxDb : IDisposable
    {
        public const string SchoolA = "GHS261103002";   // students GHS001ST001.. with outstanding dues, nothing paid; user 120 / 121 belong to it
        public const string SchoolB = "APS261013001";   // another school, for the cross-school checks
        public const long UserA = 120;
        public const long UserB = 9;
        public const long YearA = 41;

        public static readonly string? Server = ReadServer();
        private static int _n;

        private readonly string _name;
        public string Conn { get; }

        private static string? ReadServer()
        {
            var s = Environment.GetEnvironmentVariable("SMSS_TEST_PG");
            if (string.IsNullOrWhiteSpace(s)) return null;
            var b = new NpgsqlConnectionStringBuilder(s);
            if (b.Port == 5432) throw new InvalidOperationException("SMSS_TEST_PG points at port 5432: the database tests only run against a scratch server on another port.");
            return s;
        }

        public TaxDb()
        {
            var template = Environment.GetEnvironmentVariable("SMSS_TEST_TEMPLATE") ?? "smss_tpl";
            if (!template.StartsWith("smss_tpl")) throw new InvalidOperationException("The template database must be named smss_tpl*.");
            _name = $"smss_t_{Environment.ProcessId}_{Interlocked.Increment(ref _n)}_{Guid.NewGuid():N}"[..40];
            var admin = new NpgsqlConnectionStringBuilder(Server!) { Database = "postgres", Pooling = false };
            using (var c = new NpgsqlConnection(admin.ConnectionString))
            {
                c.Open();
                using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_name}\" TEMPLATE \"{template}\"", c) { CommandTimeout = 300 };   // many tests clone the template at once: the default 30 s is not always enough
                cmd.ExecuteNonQuery();
            }
            Conn = new NpgsqlConnectionStringBuilder(Server!) { Database = _name, MaxPoolSize = 30 }.ConnectionString;
        }

        public void Dispose()
        {
            NpgsqlConnection.ClearAllPools();
            var admin = new NpgsqlConnectionStringBuilder(Server!) { Database = "postgres", Pooling = false };
            using var c = new NpgsqlConnection(admin.ConnectionString);
            c.Open();
            using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", c) { CommandTimeout = 300 };
            cmd.ExecuteNonQuery();
        }

        public dbContext Ctx() => new(new DbContextOptionsBuilder<dbContext>().UseNpgsql(Conn).Options);

        public FeePaymentService Payments() { var c = Ctx(); return new FeePaymentService(new FeePaymentRepository(c), new StudentFeeRepository(c), NullLogger<FeePaymentService>.Instance, new LateFeeRepository(c)); }
        public TaxTypeService Taxes() => new(new TaxTypeRepository(Ctx()), NullLogger<TaxTypeService>.Instance);
        public FeeReportService Reports() { var c = Ctx(); return new FeeReportService(new FeeReportRepository(c), new StudentRepository(c), NullLogger<FeeReportService>.Instance); }

        public async Task<TaxTypeDto> NewTax(string school, string name, string code, decimal rate)
        {
            var r = await Taxes().CreateAsync(school, new TaxTypeRequest { TaxName = name, TaxCode = code, RatePercent = rate }, default);
            Assert.True(r.Status, r.Message);
            return r.Data!;
        }

        public Task<ApiResponse<FeeReceiptDto>> Pay(string student, decimal fee, long? taxTypeId = null, List<PaymentAllocationRequest>? allocations = null, string school = SchoolA, long user = UserA) =>
            Payments().RecordPaymentAsync(school, student, user, new RecordPaymentRequest { Amount = fee, PaymentMode = "Cash", TaxTypeId = taxTypeId, Allocations = allocations }, default);

        public T Scalar<T>(string sql)
        {
            using var c = new NpgsqlConnection(Conn); c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            var v = cmd.ExecuteScalar();
            return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Exec(string sql)
        {
            using var c = new NpgsqlConnection(Conn); c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }

        /// <summary>The student's dues, oldest first, as (id, due, paid).</summary>
        public List<(long Id, decimal Due, decimal Paid)> Dues(string student)
        {
            var list = new List<(long, decimal, decimal)>();
            using var c = new NpgsqlConnection(Conn); c.Open();
            using var cmd = new NpgsqlCommand($"select fee_due_id, amount_due, amount_paid from tb_student_fee_dues where student_id='{student}' order by due_date, fee_due_id", c);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt64(0), r.GetDecimal(1), r.GetDecimal(2)));
            return list;
        }
    }
}
