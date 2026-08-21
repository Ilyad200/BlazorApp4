namespace BlazorApp4.Services
{
    public class CompareScrollService
    {
        public bool IsEnabled { get; private set; }

        public event Action<bool>? OnChanged;

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled)
                return;

            IsEnabled = enabled;
            OnChanged?.Invoke(enabled);
        }

        public void Toggle()
        {
            SetEnabled(!IsEnabled);
        }
    }
}
