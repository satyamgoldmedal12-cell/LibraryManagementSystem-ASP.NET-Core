(() => {
    "use strict";
    const input = document.getElementById("coverImage");
    const preview = document.getElementById("cover-upload-preview");
    const message = document.getElementById("cover-preview-message");
    if (!input || !preview || !message) return;
    let selection = 0;
    input.addEventListener("change", () => {
        const current = ++selection;
        preview.hidden = true; preview.removeAttribute("src");
        message.textContent = ""; input.setCustomValidity("");
        const file = input.files[0];
        if (!file) return;
        if (!/\.(jpe?g|png)$/i.test(file.name) || file.size === 0 || file.size > 2 * 1024 * 1024) {
            message.textContent = "Choose a JPEG or PNG cover up to 2 MB.";
            input.setCustomValidity(message.textContent); input.reportValidity(); return;
        }
        const remove = document.getElementById("removeCover");
        if (remove) remove.checked = false;
        const reader = new FileReader();
        reader.addEventListener("load", () => {
            if (current !== selection) return;
            preview.src = reader.result; preview.hidden = false;
            message.textContent = "Selected cover preview. Save book to upload it. The server validates the image.";
        });
        reader.addEventListener("error", () => { if (current === selection) message.textContent = "The preview could not be loaded. Select the image again."; });
        reader.readAsDataURL(file);
    });
})();
