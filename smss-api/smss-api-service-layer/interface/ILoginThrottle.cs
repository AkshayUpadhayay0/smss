namespace smss_api_service_layer.@interface
{
    /// <summary>
    /// Slows down password guessing on POST /Auth/login. Counts FAILED sign-ins per login id and per client address inside a sliding window; over the limit the answer is
    /// "wait N seconds", never a permanent lock. The count is by the id the caller TYPED (known or not), so a throttled answer never says whether the id exists.
    /// </summary>
    public interface ILoginThrottle
    {
        /// <summary>How long this login id / client must wait before another attempt is even looked at, or null when the attempt may go ahead.</summary>
        TimeSpan? RetryAfter(string loginId, string clientKey);
        /// <summary>A sign-in with this id failed (wrong password or unknown id: the same thing, on purpose).</summary>
        void RecordFailure(string loginId, string clientKey);
        /// <summary>The right password was given: forget this login id's failures. The client address's count is NOT reset (a valid account must not wipe an attacker's count).</summary>
        void RecordSuccess(string loginId);
        /// <summary>Forget a login id's failures (an admin reset the password, so the person must be able to sign in at once).</summary>
        void ClearLogin(string loginId);
    }
}
