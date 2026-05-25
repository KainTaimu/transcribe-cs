namespace ASTranscriber.Transcription;

public interface ITranscriberFactory
{
    public ITranscriber Create(ITranscriberOptions options);
}
