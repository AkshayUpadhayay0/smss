using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// EVERYTHING a transfer certificate prints, already as text, frozen at issue time and stored as JSON. Every later copy (the original again, a duplicate) is drawn from this and from nothing else, so a later
    /// edit of the student, the school or the principal can never change an issued certificate.
    /// </summary>
    public class TcSnapshot
    {
        public string CertificateNumber { get; set; } = "";
        public string SchoolName { get; set; } = "";
        public string SchoolCode { get; set; } = "";
        public string? Board { get; set; }
        public List<string> SchoolAddress { get; set; } = new();
        public string? SchoolContact { get; set; }
        public string StudentName { get; set; } = "";
        public string AdmissionNumber { get; set; } = "";
        public string? Gender { get; set; }
        public string DateOfBirth { get; set; } = "";
        public string DateOfBirthInWords { get; set; } = "";
        public string? FatherName { get; set; }
        public string? MotherName { get; set; }
        public string? GuardianName { get; set; }
        public string AdmissionDate { get; set; } = "";
        public string LastClass { get; set; } = "";
        public string AcademicYear { get; set; } = "";
        public string Outcome { get; set; } = "";             // Left / Transferred out / Passed out
        public string LeavingDate { get; set; } = "";
        public string? LeavingReason { get; set; }
        public string PromotionStatus { get; set; } = "";
        public string Conduct { get; set; } = "";
        public string FeeDues { get; set; } = "";              // "Cleared" or "Not cleared": nothing else about money is printed
        public string? Remarks { get; set; }
        public string IssueDate { get; set; } = "";
        public string PrincipalName { get; set; } = "";
        // Optional: only in the snapshot of the certificate they were typed for, printed only when filled in (an older certificate simply has none of them)
        public string? Affiliation { get; set; }
        public string? UdiseCode { get; set; }
        public string? BoardSchoolCode { get; set; }
        public string? Nationality { get; set; }
        public string? Religion { get; set; }
        public string? Category { get; set; }
        public string? QualifiedForPromotion { get; set; }     // "Yes" / "Yes, to Class 6" / "No"
        public string? WorkingDays { get; set; }
        public string? DaysPresent { get; set; }
        public string? FeeConcession { get; set; }
        public string? ApplicationDate { get; set; }
        public string? BoardConduct { get; set; }
    }

    /// <summary>Dates in words for the certificate ("Fifth March Two Thousand Twelve").</summary>
    public static class NumberWords
    {
        private static readonly string[] Ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        private static readonly string[] Tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
        private static readonly string[] Ordinals =
        {
            "", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth", "Sixteenth", "Seventeenth",
            "Eighteenth", "Nineteenth", "Twentieth", "Twenty First", "Twenty Second", "Twenty Third", "Twenty Fourth", "Twenty Fifth", "Twenty Sixth", "Twenty Seventh", "Twenty Eighth", "Twenty Ninth", "Thirtieth", "Thirty First",
        };

        public static string Cardinal(int n)
        {
            if (n <= 0) return "Zero";
            var parts = new List<string>();
            if (n >= 1000) { parts.Add(Ones[n / 1000] + " Thousand"); n %= 1000; }
            if (n >= 100) { parts.Add(Ones[n / 100] + " Hundred"); n %= 100; }
            if (n >= 20) { parts.Add(Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "")); }
            else if (n > 0) parts.Add(Ones[n]);
            return string.Join(" ", parts);
        }

        public static string DateInWords(DateOnly d) => $"{Ordinals[d.Day]} {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(d.Month)} {Cardinal(d.Year)}";
    }

    /// <summary>The transfer certificate as an A4 PDF (QuestPDF), drawn from a snapshot only. <see cref="Lines"/> is the same content as plain text, which is what the tests compare.</summary>
    public static class TransferCertificatePdf
    {
        static TransferCertificatePdf() => PdfBootstrap.Ensure();

        /// <summary>The marking printed on a copy: null for the original, "DUPLICATE COPY No. N" for a duplicate.</summary>
        public static string? CopyLabel(int duplicateNo) => duplicateNo > 0 ? $"DUPLICATE COPY No. {duplicateNo}" : null;

        /// <summary>Every line printed, in order, as text (header, number, marking, the numbered fields, the declaration, the signature). Rendering the PDF uses exactly these.</summary>
        public static List<(string Label, string Value)> Fields(TcSnapshot s)
        {
            var parents = string.Join(" / ", new[] { s.FatherName == null ? null : "Father: " + s.FatherName, s.MotherName == null ? null : "Mother: " + s.MotherName }.Where(x => x != null));
            if (parents.Length == 0) parents = s.GuardianName ?? "";
            var rows = new List<(string, string)>
            {
                ("Name of the student", s.StudentName),
                ("Admission number", s.AdmissionNumber),
            };
            if (!string.IsNullOrWhiteSpace(s.Gender)) rows.Add(("Gender", s.Gender!));
            if (!string.IsNullOrWhiteSpace(s.Nationality)) rows.Add(("Nationality", s.Nationality!));
            if (!string.IsNullOrWhiteSpace(s.Religion)) rows.Add(("Religion", s.Religion!));
            if (!string.IsNullOrWhiteSpace(s.Category)) rows.Add(("Category", s.Category!));
            rows.Add(("Name of parent / guardian", parents));
            rows.Add(("Date of birth (in figures)", s.DateOfBirth));
            rows.Add(("Date of birth (in words)", s.DateOfBirthInWords));
            rows.Add(("Date of admission", s.AdmissionDate));
            rows.Add(("Class last studied", s.LastClass));
            rows.Add(("Academic year", s.AcademicYear));
            rows.Add(("Reason for leaving", s.LeavingReason ?? s.Outcome));
            rows.Add(("Date of leaving", s.LeavingDate));
            rows.Add(("Promotion", s.PromotionStatus));
            if (!string.IsNullOrWhiteSpace(s.QualifiedForPromotion)) rows.Add(("Qualified for promotion to the higher class", s.QualifiedForPromotion!));
            if (!string.IsNullOrWhiteSpace(s.WorkingDays)) rows.Add(("Total working days", s.WorkingDays!));
            if (!string.IsNullOrWhiteSpace(s.DaysPresent)) rows.Add(("Days present", s.DaysPresent!));
            if (!string.IsNullOrWhiteSpace(s.FeeConcession)) rows.Add(("Fee concession availed", s.FeeConcession!));
            if (!string.IsNullOrWhiteSpace(s.ApplicationDate)) rows.Add(("Date of application for the certificate", s.ApplicationDate!));
            rows.Add(("Conduct", s.Conduct));
            if (!string.IsNullOrWhiteSpace(s.BoardConduct)) rows.Add(("General conduct (board wording)", s.BoardConduct!));
            rows.Add(("Fee dues", s.FeeDues));
            if (!string.IsNullOrWhiteSpace(s.Remarks)) rows.Add(("Remarks", s.Remarks!));
            return rows;
        }

        /// <summary>"Affiliation No. X  |  UDISE Code Y  |  Board School Code Z", only the ones filled in; null when none.</summary>
        public static string? SchoolIds(TcSnapshot s)
        {
            var parts = new[] { string.IsNullOrWhiteSpace(s.Affiliation) ? null : "Affiliation No. " + s.Affiliation, string.IsNullOrWhiteSpace(s.UdiseCode) ? null : "UDISE Code " + s.UdiseCode,
                                string.IsNullOrWhiteSpace(s.BoardSchoolCode) ? null : "Board School Code " + s.BoardSchoolCode }.Where(p => p != null);
            var text = string.Join("  |  ", parts);
            return text.Length == 0 ? null : text;
        }

        public static List<string> Lines(TcSnapshot s, int duplicateNo)
        {
            var lines = new List<string> { s.SchoolName };
            lines.AddRange(s.SchoolAddress);
            if (!string.IsNullOrWhiteSpace(s.Board)) lines.Add(s.Board!);
            var ids = SchoolIds(s);
            if (ids != null) lines.Add(ids);
            if (!string.IsNullOrWhiteSpace(s.SchoolContact)) lines.Add(s.SchoolContact!);
            lines.Add("TRANSFER CERTIFICATE");
            lines.Add("Certificate No. " + s.CertificateNumber);
            var label = CopyLabel(duplicateNo);
            if (label != null) lines.Add(label);
            var n = 1;
            foreach (var (l, v) in Fields(s)) lines.Add($"{n++}. {l}: {v}");
            lines.Add("Date of issue: " + s.IssueDate);
            lines.Add("Principal: " + s.PrincipalName);
            return lines;
        }

        public static byte[] Render(TcSnapshot s, int duplicateNo)
        {
            var label = CopyLabel(duplicateNo);
            return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(18, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Hind").FontSize(11).FontColor("#1E1B2E"));
                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().AlignCenter().Text(s.SchoolName).FontFamily("PT Serif").FontSize(18).Bold();
                    foreach (var a in s.SchoolAddress) col.Item().AlignCenter().Text(a).FontSize(10);
                    if (!string.IsNullOrWhiteSpace(s.Board)) col.Item().AlignCenter().Text(s.Board!).FontSize(10);
                    var schoolIds = SchoolIds(s);
                    if (schoolIds != null) col.Item().AlignCenter().Text(schoolIds).FontSize(10);
                    if (!string.IsNullOrWhiteSpace(s.SchoolContact)) col.Item().AlignCenter().Text(s.SchoolContact!).FontSize(9).FontColor("#5B5870");
                    col.Item().PaddingTop(10).AlignCenter().Text("TRANSFER CERTIFICATE").FontFamily("PT Serif").FontSize(16).Bold().LetterSpacing(0.05f);
                    col.Item().AlignCenter().Text("Certificate No. " + s.CertificateNumber).FontSize(11).SemiBold();
                    if (label != null) col.Item().PaddingTop(4).AlignCenter().Border(1).BorderColor("#B91C1C").PaddingVertical(3).PaddingHorizontal(10).Text(label).FontSize(12).Bold().FontColor("#B91C1C");
                    col.Item().PaddingTop(8).LineHorizontal(0.7f).LineColor("#6D28D9");
                    var n = 1;
                    col.Item().PaddingTop(6).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(24); c.RelativeColumn(2.2f); c.RelativeColumn(3.2f); });
                        foreach (var (l, v) in Fields(s))
                        {
                            t.Cell().PaddingVertical(3).Text((n++) + ".");
                            t.Cell().PaddingVertical(3).Text(l);
                            t.Cell().PaddingVertical(3).Text(v).SemiBold();
                        }
                    });
                    col.Item().PaddingTop(10).Text($"This is to certify that the above particulars are correct according to the school records. The student is hereby issued this certificate on {s.IssueDate}.").FontSize(10);
                    col.Item().PaddingTop(46).Row(r =>
                    {
                        r.RelativeItem().Text("Date of issue: " + s.IssueDate);
                        r.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().LineHorizontal(0.6f);
                            c.Item().AlignRight().Text("Principal").SemiBold();
                            c.Item().AlignRight().Text(s.PrincipalName).FontSize(10);
                        });
                    });
                });
            })).GeneratePdf();
        }
    }
}
