using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public interface ISetupStep
    {
        string Title { get; }
        string Description { get; }
        Color AccentColor { get; }
        bool IsRequired { get; }
        bool IsCompleted { get; }
        bool IsSkipped { get; }

        void OnDraw();
        void OnSkip();
    }
}
