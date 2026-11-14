using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.constants;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>New admissions default to New Admission, the existing-student paths are always Existing Student; both are found by CODE and refuse clearly when missing / inactive.</summary>
    public class AdmissionTypeDefaultsTests
    {
        private const long NewId = 11, ExistingId = 12;

        private static LutAdmissionType Type(long id, string code, bool active = true) => new() { AdmissionTypeId = id, TypeCode = code, TypeName = AdmissionTypeCodes.LabelOf(code), IsActive = active };

        /// <summary>Answers a by-code lookup from a table of types and records which codes were asked for.</summary>
        private static (Func<object?[], object?> Answer, List<string> Asked) ByCode(params LutAdmissionType[] types)
        {
            var asked = new List<string>();
            return (a => { var code = (string)a[0]!; asked.Add(code); return types.FirstOrDefault(t => t.TypeCode == code); }, asked);
        }

        private static AdmissionService Admissions(Func<object?[], object?> byCode, long currentTypeId = 0)
        {
            var repo = Fake.Of<IAdmissionRepository>(new()
            {
                ["GetAdmissionTypeByCodeAsync"] = byCode,
                ["AcademicYearBelongsToSchoolAsync"] = true,
                ["ClassBelongsToSchoolAsync"] = true,
                ["AdmissionTypeUsableAsync"] = (Func<object?[], object?>)(a => (bool)a[1]! || (long)a[0]! is NewId or ExistingId),
                ["GetByIdAsync"] = currentTypeId == 0 ? null : new TbAdmission { AdmissionId = 10, SchoolId = "SCH1", AdmissionTypeId = currentTypeId, StatusId = 1, ApplicantFirstName = "A", ApplicantLastName = "B" },
            });
            return new AdmissionService(repo, Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
        }

        private static SubmitAdmissionRequestDto Req(long? typeId) => new()
        {
            AcademicYearId = 1, AppliedClassId = 1, AdmissionTypeId = typeId, ApplicantFirstName = "Asha", ApplicantLastName = "Rao",
            DateOfBirth = new DateTime(2015, 1, 1), GuardianName = "G", GuardianRelationship = "Father", GuardianMobileNumber = "9999999999"
        };

        [Fact]
        public async Task NewApplication_WithoutAType_GetsNewAdmission_FoundByCode()
        {
            var (answer, asked) = ByCode(Type(NewId, "NEW_ADMISSION"), Type(ExistingId, "EXISTING_STUDENT"));
            var req = Req(null);
            await Admissions(answer).SubmitAsync("SCH1", req, CancellationToken.None);   // goes on to fail later on the bare fake; only the defaulting matters here
            Assert.Equal(NewId, req.AdmissionTypeId);
            Assert.Equal(new[] { AdmissionTypeCodes.NewAdmission }, asked);
        }

        [Fact]
        public async Task NewApplication_WithAType_KeepsWhatWasSent_AndLooksNothingUp()
        {
            var (answer, asked) = ByCode(Type(NewId, "NEW_ADMISSION"));
            var req = Req(ExistingId);
            await Admissions(answer).SubmitAsync("SCH1", req, CancellationToken.None);
            Assert.Equal(ExistingId, req.AdmissionTypeId);
            Assert.Empty(asked);
        }

        [Theory]
        [InlineData(false, "inactive")]
        [InlineData(true, "not set up")]
        public async Task NewApplication_WhenNewAdmissionIsInactiveOrMissing_IsRefusedClearly(bool missing, string wording)
        {
            var (answer, _) = ByCode(missing ? Array.Empty<LutAdmissionType>() : new[] { Type(NewId, "NEW_ADMISSION", active: false) });
            var r = await Admissions(answer).SubmitAsync("SCH1", Req(null), CancellationToken.None);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains("'New Admission'", r.Message);
            Assert.Contains(wording, r.Message);
            Assert.Contains("contact support", r.Message);
        }

        [Fact]
        public async Task Edit_WithoutAType_KeepsTheCurrentOne_EvenIfNowDeactivated()
        {
            var (answer, asked) = ByCode(Type(NewId, "NEW_ADMISSION", active: false));
            var req = Req(null);
            await Admissions(answer, currentTypeId: 99).UpdateAsync("SCH1", 10, req, CancellationToken.None);
            Assert.Equal(99, req.AdmissionTypeId);
            Assert.Empty(asked);
        }

        // ---------------------------------------------------------------- existing-student path
        private static StudentOnboardingService Onboarding(Func<object?[], object?> byCode) =>
            new(Fake.Of<IAdmissionRepository>(new() { ["GetGeneralStatusIdAsync"] = (int?)1, ["GetAdmissionTypeByCodeAsync"] = byCode }),
                Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentOnboardingService>.Instance);

        [Fact]
        public void ExistingStudentRequest_HasNoAdmissionTypeField_SoAClientCannotChooseOne()
        {
            Assert.DoesNotContain(typeof(DirectStudentEntryRequestDto).GetProperties(), p => p.Name.Contains("AdmissionType", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ExistingStudentPath_AlwaysAsksForExistingStudent_NeverNewAdmission()
        {
            var (answer, asked) = ByCode(Type(NewId, "NEW_ADMISSION"));   // only New Admission exists: asking for the wrong one would "succeed"
            var r = await Onboarding(answer).CreateDirectEntryAsync("SCH1", new DirectStudentEntryRequestDto(), CancellationToken.None);
            Assert.Equal(new[] { AdmissionTypeCodes.ExistingStudent }, asked);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains("'Existing Student'", r.Message);
        }

        [Theory]
        [InlineData(false, "inactive")]
        [InlineData(true, "not set up")]
        public async Task ExistingStudentPath_WhenTheTypeIsInactiveOrMissing_IsRefusedClearly(bool missing, string wording)
        {
            var (answer, _) = ByCode(missing ? Array.Empty<LutAdmissionType>() : new[] { Type(ExistingId, "EXISTING_STUDENT", active: false) });
            var r = await Onboarding(answer).CreateDirectEntryAsync("SCH1", new DirectStudentEntryRequestDto(), CancellationToken.None);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains(wording, r.Message);
            Assert.Contains("contact support", r.Message);
        }

        // ---------------------------------------------------------------- the shared resolver (also used by the student import)
        [Fact]
        public async Task Resolver_ReturnsTheActiveTypeByCode_AndExplainsMissingOrInactive()
        {
            var ok = await AdmissionTypeDefaults.ResolveAsync((c, _) => Task.FromResult<LutAdmissionType?>(Type(ExistingId, c)), AdmissionTypeCodes.ExistingStudent, default);
            Assert.Equal(ExistingId, ok.Type!.AdmissionTypeId);
            Assert.Null(ok.Error);
            var off = await AdmissionTypeDefaults.ResolveAsync((c, _) => Task.FromResult<LutAdmissionType?>(Type(ExistingId, c, active: false)), AdmissionTypeCodes.ExistingStudent, default);
            Assert.Null(off.Type);
            Assert.Contains("'Existing Student' Admission Type is inactive", off.Error);
            var none = await AdmissionTypeDefaults.ResolveAsync((c, _) => Task.FromResult<LutAdmissionType?>(null), AdmissionTypeCodes.NewAdmission, default);
            Assert.Contains("'New Admission' Admission Type is not set up", none.Error);
        }

        [Fact]
        public void TheTwoCodes_AreTheOnesTheMigrationSeeds() =>
            Assert.Equal(new[] { "NEW_ADMISSION", "EXISTING_STUDENT" }, new[] { AdmissionTypeCodes.NewAdmission, AdmissionTypeCodes.ExistingStudent });
    }
}
