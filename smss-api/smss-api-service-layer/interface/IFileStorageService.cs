using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IFileStorageService
    {
        // Saves a school logo and returns its relative public URL
        Task<string> SaveSchoolLogoAsync(string schoolCode, Stream content, string extension, CancellationToken ct);

        // Saves a student's profile photo under {schoolCode}/students/{studentId}/ and returns its relative public URL
        Task<string> SaveStudentPhotoAsync(string schoolCode, string studentId, Stream content, string extension, CancellationToken ct);

        // Saves an employee's photo under {schoolCode}/employees/{employeeId}/ and returns its relative public URL
        Task<string> SaveEmployeePhotoAsync(string schoolCode, string employeeId, Stream content, string extension, CancellationToken ct);

        // Employee DOCUMENTS go to a PRIVATE store outside the public uploads folder (never reachable by URL). The returned storage
        // key ({schoolCode}/employees/{employeeId}/documents/<random file>) is what the database keeps; files are served only via OpenPrivate.
        Task<string> SaveEmployeeDocumentAsync(string schoolCode, string employeeId, Stream content, string extension, CancellationToken ct);
        // A homework attachment goes to the same PRIVATE store: {schoolCode}/homework/{homeworkId}/attachment_<timestamp><ext>. The returned key is what the database keeps.
        Task<string> SaveHomeworkAttachmentAsync(string schoolCode, long homeworkId, Stream content, string extension, CancellationToken ct);
        Stream? OpenPrivate(string storageKey);
        void DeletePrivate(string storageKey);

        // Best-effort delete by the URL returned from Save. Never throws.
        void Delete(string relativeUrl);
    }
}
