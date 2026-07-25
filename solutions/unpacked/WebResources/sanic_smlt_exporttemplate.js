"use strict";
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

Sanic.Smelter.EXPORT_API = "sanic_capi_smelter_exporttemplate";

Sanic.Smelter.exportTemplate = function (selectedId) {
    var id = (selectedId || "").replace(/[{}]/g, "");
    if (!id) {
        Xrm.Navigation.openAlertDialog({ text: "Seleccioná un template para exportar." });
        return;
    }

    var request = {
        TemplateId: { guid: id },
        getMetadata: function () {
            return {
                boundParameter: null,
                operationType: 0, // 0 = Action
                operationName: Sanic.Smelter.EXPORT_API,
                parameterTypes: {
                    TemplateId: { typeName: "Edm.Guid", structuralProperty: 1 }
                }
            };
        }
    };

    Xrm.WebApi.online.execute(request)
        .then(function (response) { return response.json(); })
        .then(function (result) {
            var blob = new Blob([result.Json], { type: "application/json" });
            var url = URL.createObjectURL(blob);
            var a = document.createElement("a");
            a.href = url;
            a.download = "smelter-template.json";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }, function (error) {
            Xrm.Navigation.openErrorDialog({ message: error.message });
        });
};