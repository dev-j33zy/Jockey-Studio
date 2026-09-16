import { useStore } from "../state/store";

export default function TopBar() {
  const { native, addDecks, toggleLibrary, toggleSettings, libraryOpen, settingsOpen } =
    useStore();

  return (
    <header className="topbar">
      <div className="topbar-brand">
        <span className="brand-dot" />
        <h1>Jockey Studio</h1>
        <span className="badge">{native ? "Native" : "Browser"}</span>
      </div>
      <div className="topbar-actions">
        <button className="btn" onClick={() => void addDecks(1)}>
          + Add Deck
        </button>
        <button className={`btn ${libraryOpen ? "active" : ""}`} onClick={() => toggleLibrary()}>
          Library
        </button>
        <button className={`btn ${settingsOpen ? "active" : ""}`} onClick={() => toggleSettings()}>
          Settings
        </button>
      </div>
    </header>
  );
}