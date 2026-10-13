using System.Globalization;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Temporary password for a new student login = first 4 letters of the first name (upper-case, padded with X) +
    /// date of birth as ddMM (ABHI0807). The login id is the student's own id (see StudentIdHelper). The password is
    /// single-use: the account is created with is_first_login = true, so the student must replace it before doing anything else.
    /// </summary>
    public static class StudentLoginHelper
    {
        private const int NameLength = 4;
        private const char Pad = 'X';

        public static string BuildTemporaryPassword(string firstName, DateTime dateOfBirth)
        {
            // ASCII A-Z only (same rule as the school id prefix): accented / non-Latin letters are dropped, so a name
            // with fewer than 4 usable letters is padded with X ("Om" -> "OMXX", a Devanagari name -> "XXXX")
            var letters = new List<char>(NameLength);
            foreach (var c in firstName ?? string.Empty)
            {
                if (c is >= 'A' and <= 'Z') letters.Add(c);
                else if (c is >= 'a' and <= 'z') letters.Add((char)(c - 32));
                if (letters.Count == NameLength) break;
            }
            while (letters.Count < NameLength) letters.Add(Pad);

            return new string(letters.ToArray()) + dateOfBirth.ToString("ddMM", CultureInfo.InvariantCulture);
        }
    }
}
