namespace Weather
{
    /// <summary>
    /// How bad the sky is right now. Replicated as a byte by <see cref="WeatherSystem"/>, so the
    /// order matters: everything that scales with severity (wind strength, rain emission, audio
    /// volume) reads it as a ramp from Clear up to Storm.
    /// </summary>
    public enum WeatherPhase : byte
    {
        Clear = 0,
        Rain = 1,
        Storm = 2,
    }
}
