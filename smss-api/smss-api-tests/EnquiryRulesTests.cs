using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using Xunit;

namespace smss_api_tests
{
    public class EnquiryRulesTests
    {
        private static readonly DateOnly Today = new(2026, 11, 9);

        private static CreateEnquiryRequest Contact() => new() { EnquiryType = "contact", FullName = "  Asha   Rao ", Email = "Asha@Example.com", Message = "Hello there", ConsentGiven = true };
        private static CreateEnquiryRequest Demo() => new()
        {
            EnquiryType = "DEMO", FullName = "Asha Rao", SchoolName = "Green Valley School", Email = "asha@example.com", Phone = "+91 98765 43210", ConsentGiven = true,
        };

        [Fact] public void Valid_contact_is_accepted_and_cleaned()
        {
            var r = Contact();
            Assert.Empty(EnquiryRules.CleanAndValidate(r, Today));
            Assert.Equal("CONTACT", r.EnquiryType);
            Assert.Equal("Asha Rao", r.FullName);
            Assert.Equal("asha@example.com", r.Email);
        }

        [Fact] public void Valid_demo_is_accepted() => Assert.Empty(EnquiryRules.CleanAndValidate(Demo(), Today));

        [Fact] public void Contact_needs_a_message() { var r = Contact(); r.Message = "   "; Assert.Contains("message", EnquiryRules.CleanAndValidate(r, Today).Keys); }
        [Fact] public void Demo_needs_school_and_phone() { var r = Demo(); r.SchoolName = null; r.Phone = null; var e = EnquiryRules.CleanAndValidate(r, Today); Assert.Contains("schoolName", e.Keys); Assert.Contains("phone", e.Keys); }
        [Fact] public void Consent_is_required() { var r = Contact(); r.ConsentGiven = false; Assert.Contains("consentGiven", EnquiryRules.CleanAndValidate(r, Today).Keys); }
        [Fact] public void Unknown_type_is_rejected() { var r = Contact(); r.EnquiryType = "OTHER"; Assert.Contains("enquiryType", EnquiryRules.CleanAndValidate(r, Today).Keys); }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("a@b")]
        [InlineData("a b@example.com")]
        public void Bad_email_is_rejected(string email) { var r = Contact(); r.Email = email; Assert.Contains("email", EnquiryRules.CleanAndValidate(r, Today).Keys); }

        [Theory]
        [InlineData("abc")]
        [InlineData("12")]
        [InlineData("98765<script>")]
        public void Bad_phone_is_rejected(string phone) { var r = Demo(); r.Phone = phone; Assert.Contains("phone", EnquiryRules.CleanAndValidate(r, Today).Keys); }

        [Fact] public void Html_is_stripped_from_text() { var r = Contact(); r.Message = "Hi <script>alert(1)</script>there"; EnquiryRules.CleanAndValidate(r, Today); Assert.Equal("Hi alert(1)there", r.Message); }
        [Fact] public void Overlong_name_is_rejected() { var r = Contact(); r.FullName = new string('a', 151); Assert.Contains("fullName", EnquiryRules.CleanAndValidate(r, Today).Keys); }
        [Fact] public void Past_demo_date_is_rejected() { var r = Demo(); r.PreferredDemoDate = Today.AddDays(-1); Assert.Contains("preferredDemoDate", EnquiryRules.CleanAndValidate(r, Today).Keys); }
        [Fact] public void Unknown_range_and_slot_are_rejected() { var r = Demo(); r.StudentCountRange = "lots"; r.PreferredTimeSlot = "midnight"; var e = EnquiryRules.CleanAndValidate(r, Today); Assert.Contains("studentCountRange", e.Keys); Assert.Contains("preferredTimeSlot", e.Keys); }
        [Fact] public void Demo_only_fields_are_dropped_from_a_contact()
        {
            var r = Contact(); r.StudentCountRange = "Up to 200"; r.PreferredTimeSlot = "x"; r.PreferredDemoDate = Today;
            EnquiryRules.CleanAndValidate(r, Today);
            Assert.Null(r.StudentCountRange); Assert.Null(r.PreferredTimeSlot); Assert.Null(r.PreferredDemoDate);
        }
    }
}
