using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.@interface;

namespace smss_api.Helpers
{
    public static class SetupGateExtensions
    {
        /// <summary>
        /// Call at the top of an endpoint that ADDS students, employees or transport records. Returns null when the caller may go ahead, otherwise the
        /// standard envelope with 409 SETUP_INCOMPLETE and the next step. The Super Admin is exempt. The school comes from the token only.
        /// Never add this to reads, edits of existing rows, or the setup and fee screens.
        /// </summary>
        public static async Task<IActionResult?> RequireSetupAsync(this ControllerBase controller, IOnboardingService onboarding, CancellationToken ct)
        {
            if (controller.User.IsSuperAdmin()) return null;
            var blocked = await onboarding.CheckSetupAsync(controller.User.GetSchoolId(), ct);
            return blocked == null ? null : controller.StatusCode(blocked.StatusCode, blocked);
        }
    }
}
