!(function (a) {
    "use strict";
    function n() {}
    (n.prototype.init = function () {
        a("#world-map-markers").vectorMap({
            map: "world_mill_en",
            normalizeFunction: "polynomial",
            hoverOpacity: 0.7,
            hoverColor: !1,
            regionStyle: { initial: { fill: "#d4dadd" } },
            markerStyle: { initial: { r: 9, fill: "#556ee6", "fill-opacity": 0.9, stroke: "#fff", "stroke-width": 7, "stroke-opacity": 0.4 }, hover: { stroke: "#fff", "fill-opacity": 1, "stroke-width": 1.5 } },
            backgroundColor: "transparent",
            markers: [
                { latLng: [	29, 30], name: "Egypt" },
                { latLng: [43.73, 7.41], name: "Monaco" },
                { latLng: [29, -10], name: "Moroco" },
                { latLng: [24, 56], name: "UAE" },
                { latLng: [3.2, 73.22], name: "Maldives" },
                { latLng: [35.88, 10.5], name: "Tunisia" },
                { latLng: [27, 1.51], name: "Algeria" },
                { latLng: [24, 46], name: "KSA" },
                { latLng: [40, -98.28], name: "United States" },
            ],
        })
    }),
        (a.VectorMap = new n()),
        (a.VectorMap.Constructor = n);
})(window.jQuery),
    (function () {
        "use strict";
        window.jQuery.VectorMap.init();
    })();
