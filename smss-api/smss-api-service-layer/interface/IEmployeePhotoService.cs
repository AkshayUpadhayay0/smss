namespace smss_api_service_layer.@interface
{
    /// <summary>Authenticated read of an employee's photo from private storage. The school always comes from the token; another school's employee is a 404.</summary>
    public interface IEmployeePhotoService
    {
        /// <param name="seesAnyEmployee">School Admin or Principal: any employee of the school. Everyone else only their own photo (403 otherwise).</param>
        Task<StudentPhotoResult> GetAsync(string? schoolId, long callerUserId, bool seesAnyEmployee, string employeeId, CancellationToken ct);
    }
}
