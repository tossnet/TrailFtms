// Global STOP shortcut (Space / Escape) and fullscreen.

const stopHandlers = new Map();
let nextId = 1;
let installed = false;

function isTyping(target) {
    return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement
        || target instanceof HTMLSelectElement || target?.isContentEditable;
}

function onKeyDown(e) {
    const isSpace = e.code === "Space" || e.key === " ";
    if (!isSpace && e.key !== "Escape") return;
    if (isSpace && isTyping(e.target)) return;
    // Space must not also click the focused button or scroll the page.
    e.preventDefault();
    for (const handler of stopHandlers.values()) handler.invokeMethodAsync("OnStopShortcut");
}

function onKeyUp(e) {
    if ((e.code === "Space" || e.key === " ") && !isTyping(e.target)) e.preventDefault();
}

// A click in the YouTube iframe moves the keyboard focus into it, where the page cannot hear keys.
// Take the focus back right away: the click itself has already reached the player.
function onWindowBlur() {
    setTimeout(() => {
        if (document.activeElement instanceof HTMLIFrameElement) {
            document.activeElement.blur();
            window.focus();
        }
    }, 0);
}

export function addStopShortcut(dotnet) {
    if (!installed) {
        document.addEventListener("keydown", onKeyDown);
        document.addEventListener("keyup", onKeyUp);
        window.addEventListener("blur", onWindowBlur);
        installed = true;
    }
    const id = nextId++;
    stopHandlers.set(id, dotnet);
    return id;
}

export function removeStopShortcut(id) {
    stopHandlers.delete(id);
}

export async function toggleFullscreen() {
    if (document.fullscreenElement) await document.exitFullscreen();
    else await document.documentElement.requestFullscreen();
}

// Browser prompt when leaving the page (close, reload) with unsaved changes.
function onBeforeUnload(e) {
    e.preventDefault();
}

export function setLeaveWarning(enabled) {
    if (enabled) window.addEventListener("beforeunload", onBeforeUnload);
    else window.removeEventListener("beforeunload", onBeforeUnload);
}
