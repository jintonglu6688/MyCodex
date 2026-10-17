namespace MyCodex.Domain.Enums
{
    // Keep Sound at 0 so legacy config defaults preserve existing behavior.
    public enum NotificationPromptMode
    {
        Sound = 0,
        Off = 1,
        Speech = 2,
        SoundAndSpeech = 3
    }
}
