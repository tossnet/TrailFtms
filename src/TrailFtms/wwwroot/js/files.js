// Saves text as a file on the user's device.
export function downloadText(fileName, text, mimeType = "application/json") {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
}

// Opens the picker of a (hidden) <input type="file">. Must run from a user gesture.
export function openPicker(input) {
    input.click();
}
