using FluentResults;
using PathLib;

namespace ASTranscriber.Transcription;

public interface ITranscriber
{
    /// <summary>
    /// Begin transcription.
    /// </summary>
    /// <returns>The exit code. Zero meaning success.</returns>
    public Result Start();

    /// <summary>
    /// Get the path of the resulting transcripted file in SRT format
    /// </summary>
    public IPath? GetTranscriptionSrt();
}
