// ==UserScript==
// @name         github_get_files_name
// @namespace    http://tampermonkey.net/
// @version      1.0
// @description  Изменение стиля GitHub для удобства и улучшения работы OCR бота
// @author       Woowz11
// @match        https://github.com/*/*/tree/*
// @icon         https://www.google.com/s2/favicons?bb=github.com
// @grant        GM_addStyle
// ==/UserScript==

(function() {
    'use strict';

    // language=CSS
    GM_addStyle(
`
    :root {
        --bgColor-default: #FFFFFFFF !important;
    }
    
    *{
        text-shadow: unset !important;
        letter-spacing: 3px !important;
        font-family: "Roboto Light", monospace !important;
        color: black !important;
        font-weight: 700 !important;
    }
 
    html{
        zoom: 1.5 !important;
    }
 
    html, body{
        overflow-x: hidden !important;
        width: 100% !important;
        position: relative !important;
    }

    #octonext-toggle{
        display: none;
    !important;
    }

    react-partial[partial-name="global-nav-bar"]{
        display: none !important;
    }

    div[data-component="SplitPageLayout.Content"]{
        display: none !important;
    }

    div[data-component="SplitPageLayout.Pane"]{
        --pane-width: 100% !important;
        --pane-max-width: 100% !important;
    }

    @media (min-width: 768px) {
        div[class*="CodeViewFileTreeLayout-module__sidebar"]{
            width: 100% !important;
            max-width: none !important;
        }
    }
    
    .ReposFileTreePane-module__Box_1__PpIop{
        display: none !important;
    }
    
    .ReposFileTreePane-module__FileResultsList__zmSnM{
        display: none !important;
    }
    
    span.prc-Spinner-Box-Y-ke-{
        background-color: #00FFFF !important;
        border-radius: unset !important;
        width: 16px !important;
        height: 16px !important;
    }
    
    svg.octicon-file {
        background-color: #FF0000 !important;
        border-radius: unset !important;
        width: 16px !important;
        height: 16px !important;
    }

    svg.octicon-file path {
        display: none !important;
    }

    svg.octicon-file-directory-fill {
        background-color: #FFFF00 !important;
        border-radius: unset !important;
        width: 16px !important;
        height: 16px !important;
    }

    svg.octicon-file-directory-fill path {
        display: none !important;
    }

    svg.octicon-file-directory-open-fill {
        background-color: #FF00FF !important;
        border-radius: unset !important;
        width: 16px !important;
        height: 16px !important;
    }

    svg.octicon-file-directory-open-fill path {
        display: none !important;
    }
    
    #AGENT-Debug-Square{
        position: fixed !important;
        top: 0 !important;
        right: 3px !important;
        width: 30px !important;
        height: 30px !important;
        background-color: rgb(156, 13, 5) !important;
        z-index: 2147483647 !important;
        pointer-events: none !important;
        border: none !important;
    }
`);

    const IDSquare = document.createElement("div");
    IDSquare.id = "AGENT-Debug-Square";
    document.body.appendChild(IDSquare);

})();