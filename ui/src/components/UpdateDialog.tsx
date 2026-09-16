import { useStore } from "../state/store";

export default function UpdateDialog() {
  const updateInfo = useStore((s) => s.updateInfo);
  const updateDialogOpen = useStore((s) => s.updateDialogOpen);
  const updateInProgress = useStore((s) => s.updateInProgress);
  const closeUpdateDialog = useStore((s) => s.closeUpdateDialog);
  const installUpdate = useStore((s) => s.installUpdate);

  if (!updateDialogOpen || !updateInfo) return null;

  return (
    <div className="update-veil" onClick={closeUpdateDialog}>
      <div className="update-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="update-dialog-head">
          <h2>Update available</h2>
          <button className="tool-btn" title="Close" onClick={closeUpdateDialog}>
            x
          </button>
        </div>

        <div className="update-versions">
          <span className="update-ver-current">v{updateInfo.currentVersion}</span>
          <span className="update-arrow">to</span>
          <span className="update-ver-latest">v{updateInfo.latestVersion}</span>
        </div>

        <div className="update-notes">
          {updateInfo.releaseNotes || "No release notes for this version."}
        </div>

        <div className="update-actions">
          <button className="btn" onClick={closeUpdateDialog} disabled={updateInProgress}>
            Update Later
          </button>
          <button
            className="btn primary"
            onClick={() => void installUpdate()}
            disabled={updateInProgress}
          >
            {updateInProgress ? "Downloading & installing…" : "Install Now"}
          </button>
        </div>

        {updateInProgress ? (
          <p className="update-progress-note">
            Downloading the installer and closing the app. It will relaunch automatically once
            installed.
          </p>
        ) : null}
      </div>
    </div>
  );
}