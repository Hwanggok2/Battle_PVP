mergeInto(LibraryManager.library, {
  BattlePvpPerformance_Download: function (filenamePtr, contentsPtr, mimePtr) {
    var blob = new Blob([UTF8ToString(contentsPtr)], { type: UTF8ToString(mimePtr) });
    var url = URL.createObjectURL(blob);
    var link = document.createElement('a');
    link.href = url;
    link.download = UTF8ToString(filenamePtr);
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  }
});
