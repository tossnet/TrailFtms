// localStorage that never throws: when the browser blocks site data (cookie settings, strict private
// mode, company policy) even reading window.localStorage raises a SecurityError. Fall back to memory:
// the app keeps working, it just forgets settings and imported programs on reload.

const memory = new Map();

function local() {
    try {
        return window.localStorage;
    } catch {
        return null;
    }
}

export function getItem(key) {
    try {
        const value = local()?.getItem(key);
        if (value !== undefined && value !== null) return value;
    } catch {
        // Blocked or broken storage: use memory.
    }
    return memory.get(key) ?? null;
}

export function setItem(key, value) {
    memory.set(key, value);
    try {
        local()?.setItem(key, value);
    } catch {
        // Blocked, or quota exceeded: memory only.
    }
}
