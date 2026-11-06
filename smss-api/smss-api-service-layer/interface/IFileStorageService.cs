using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IFileStorageService
    {
        // Saves a school logo and returns its relative public URL
        Task<string> SaveSchoolLogoAsync(string schoolCode, Stream content, string extension, CancellationToken ct);

        // Saves a student's profile photo to the PRIVATE store under {schoolCode}/students/{studentId}/ and returns its storage KEY (never a public URL)
        Task<string> SaveStudentPhotoAsync(string schoolCode, string studentId, Stream content, string extension, CancellationToken ct);

        // Saves an employee's photo under {schoolCode}/employees/{employeeId}/ and returns its relative public URL
        Task<string> SaveEmployeePhotoAsync(string schoolCode, string employeeId, Stream content, string extension, CancellationToken ct);

        // Employee DOCUMENTS go to a PRIVATE store outside the public uploads folder (never reachable by URL). The returned storage
        // key ({schoolCode}/employees/{employeeId}/documents/<random file>) is what the database keeps; files are served only via OpenPrivate.
        Task<string> SaveEmployeeDocumentAsync(string schoolCode, string employeeId, Stream content, string extension, CancellationToken ct);
        // A homework attachment goes to the same PRIVATE store: {schoolCode}/homework/{homeworkId}/attachment_<timestamp><ext>. The returned key is what the database keeps.
        Task<string> SaveHomeworkAttachmentAsync(string schoolCode, long homeworkId, Stream content, string extension, CancellationToken ct);
        // An admission application's documents go to the same PRIVATE store: {schoolCode}/admissions/{admissionId}/documents/<random file>.
        Task<string> SaveAdmissionDocumentAsync(string schoolCode, long admissionId, Stream content, string extension, CancellationToken ct);
        // A school's principal signature for ID cards goes to the same PRIVATE store: {schoolCode}/id-card/signature_<timestamp><ext>. The returned key is what the database keeps.
        Task<string> SavePrincipalSignatureAsync(string schoolCode, Stream content, string extension, CancellationToken ct);
        Stream? OpenPrivate(string storageKey);
        // A stored student photo: a private key, or (until the one-off migration has run) a legacy public /uploads path. Null when missing. Never throws.
        Stream? OpenStudentPhoto(string stored);
        void DeleteStudentPhoto(string stored);
        void DeletePrivate(string storageKey);

        // Server-side read of a file saved under the uploads root, by the relative URL the database keeps (logo, student photo). Used to embed
        // images in generated documents; it creates no new URL. Null when the file is missing or the URL points outside the uploads root. Never throws.
        Stream? OpenUpload(string relativeUrl);

        // Best-effort delete by the URL returned from Save. Never throws.
        void Delete(string relativeUrl);
    }
}
