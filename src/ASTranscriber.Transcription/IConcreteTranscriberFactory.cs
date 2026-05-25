namespace ASTranscriber.Transcription;

public interface IConcreteTranscriberFactory
{
    ITranscriber CreateTranscriber(ITranscriberOptions options);
}
