var ws = null;

window.onload = function () {
    ws = new WebSocket("<%address%>");
    ws.onopen = function () {
        //ws.send("test");
    };
    ws.onerror = function (error) {
        alert("WebSocket Error " + error);
    };
    ws.onmessage = function (e) {
        if (e.data.indexOf("main", 0) == 0) {
            var html = e.data.substring(5);

            $("#main").html(html);
        }
        else if (e.data.indexOf("script", 0) == 0) {
            var script = e.data.substring(7);

            eval(script);
        }
    };
};

window.onunload = function () {
    if (ws != null)
        ws.close();
};