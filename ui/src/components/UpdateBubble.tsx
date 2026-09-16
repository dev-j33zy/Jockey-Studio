import { useStore } from "../state/store";

export default function UpdateBubble() {
  const updateInfo = useStore((s) => s.updateInfo);
  const openUpdateDialog = useStore((s) => s.openUpdateDialog);
  const dismissUpdate = useStore((s) => s.dismissUpdate);

  if (!updateInfo) return null;

  return (
    <div
      className="update-bubble"
      role="button"
      tabIndex={0}
      title="Click to view the update"
      onClick={() => openUpdateDialog()}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          openUpdateDialog();
        }
      }}
    >
      <div className="update-bubble-text">
        <span className="update-bubble-title">Update available</span>
        <span className="update-bubble-version">v{updateInfo.latestVersion}</span>
      </div>
      <button
        className="update-bubble-dismiss"
        title="Dismiss update notification"
        onClick={(e) => {
          e.stopPropagation();
          dismissUpdate();
        }}
      >
        x
      </button>
    </div>
  );
}