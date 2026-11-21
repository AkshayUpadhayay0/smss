namespace smss_api_service_layer.@interface
{
    /// <summary>The server-side cache of the public Partner Schools list. Anything that changes what that list shows must call <see cref="Invalidate"/>.</summary>
    public interface IPartnerSchoolCache
    {
        void Invalidate();
    }
}
