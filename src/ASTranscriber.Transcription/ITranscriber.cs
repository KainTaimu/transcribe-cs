using FluentResults;
using PathLib;

namespace ASTranscriber.Transcription;

public interface ITranscriber
{
    /// <summary>
    /// Begin transcription of a single audio file.
    /// </summary>
    /// <param name="inputFile">Path to the audio file to transcribe</param>
    /// <param name="outputDirectory">Path to the output directory for results</param>
    /// <param name="modelPath">Path to the Whisper model file</param>
    /// <returns>The exit code. Zero meaning success.</returns>
    public Result Start(IPath inputFile, IPath outputDirectory, IPath modelPath);

    /// <summary>
    /// Get the path of the resulting transcripted file in SRT format
    /// </summary>
    public IPath? GetTranscriptionSrt();
}
