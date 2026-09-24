// Opens the native file picker for a hidden <InputFile>, invoked from C# via IJSRuntime.
// A <label for="..."> wrapping a styled button does NOT reliably forward its click to the target
// input in every browser once the label contains another interactive element (a <button>) - clicking
// the input directly here sidesteps that entirely.
window.triggerFileInputClick = function (elementId) {
    document.getElementById(elementId)?.click();
};
