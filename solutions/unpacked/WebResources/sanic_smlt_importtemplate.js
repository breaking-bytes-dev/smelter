"use strict";
var Sanic = window.Sanic || {};
Sanic.Smelter = Sanic.Smelter || {};

Sanic.Smelter.IMPORT_API = "sanic_capi_smelter_importtemplate";

Sanic.Smelter.importTemplate = function (primaryControl) {
    var input = document.createElement("input");
    input.type = "file";
    input.accept = ".json,application/json";

    input.onchange = function (ev) {
        var file = ev.target.files[0];
        if (!file) { return; }

        var reader = new FileReader();
        reader.onload = function () {
            var request = {
                Json: reader.result,
                getMetadata: function () {
                    return {
                        boundParameter: null,
                        operationType: 0,
                        operationName: Sanic.Smelter.IMPORT_API,
                        parameterTypes: {
                            Json: { typeName: "Edm.String", structuralProperty: 1 }
                        }
                    };
                }
            };

            Xrm.WebApi.online.execute(request)
                .then(function (response) { return response.json(); })
                .then(function (result) {
                    var msg = "Template importado correctamente.";
                    if (result.Warnings && result.Warnings.length > 0) {
                        msg += "\n\nAdvertencias:\n" + result.Warnings;
                    }
                    return Xrm.Navigation.openAlertDialog({ text: msg });
                })
                .then(function () {
                    if (primaryControl && primaryControl.refresh) { primaryControl.refresh(); }
                }, function (error) {
                    Xrm.Navigation.openErrorDialog({ message: error.message });
                });
        };
        reader.readAsText(file);
    };

    input.click();
};