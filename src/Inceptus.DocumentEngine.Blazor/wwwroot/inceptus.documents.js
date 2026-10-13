export function downloadFile(bytes, contentType, fileName) {
    if (!(bytes instanceof Uint8Array)) {
        throw new TypeError("Download bytes must be a Uint8Array.");
    }
    if (typeof contentType !== "string" || contentType.trim().length === 0) {
        throw new TypeError("Download content type must be a non-empty string.");
    }
    if (typeof fileName !== "string" || fileName.trim().length === 0) {
        throw new TypeError("Download file name must be a non-empty string.");
    }

    const blob = new Blob([bytes], { type: contentType });
    const objectUrl = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    try {
        anchor.href = objectUrl;
        anchor.download = fileName;
        anchor.hidden = true;
        document.body.appendChild(anchor);
        anchor.click();
    } finally {
        anchor.remove();
        URL.revokeObjectURL(objectUrl);
    }
}


