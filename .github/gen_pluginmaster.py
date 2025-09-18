#!/usr/bin/env python3
from requests import get
from os import environ
from json import load,dump
from time import time

ROOT = environ.get("GITHUB_WORKSPACE", ".")
REPO = "PunishXIV/Questionable"
CSPROJ = "Questionable/Questionable.csproj"


with open(f"{ROOT}/pluginmaster.json") as f:
    pluginmaster = load(f)

def get_dalamud_api_level(release):
    head = get(f"https://raw.githubusercontent.com/{REPO}/refs/tags/{release['tag_name']}/{CSPROJ}").text
    return head.split('<Project Sdk="Dalamud.NET.Sdk/')[1].split(".")[0]

def get_changelog(release):
    return release["body"].replace("\r","").split("\n### Changes in this release\n")[1].split("\n### Installation Files")[0].strip()

def get_releases():
    data = get(f"https://api.github.com/repos/{REPO}/releases").json()
    testing, latest = None, None
    downloads = 0
    for r in data:
        if not testing and r["prerelease"] is True:
            testing = r
            testing["_dalamud"] = get_dalamud_api_level(testing)
            testing["_changelog"] = get_changelog(testing)
        elif not latest and r["prerelease"] is False:
            latest = r
            latest["_dalamud"] = get_dalamud_api_level(latest)
            latest["_changelog"] = get_changelog(latest)
        for a in r["assets"]:
            downloads += a["download_count"]
    return testing,latest,downloads

def main():
    testing,latest,downloads = get_releases()
    pluginmaster[0].update({
        "DownloadCount": downloads,
        "LastUpdate": int(time()),
        "Changelog": "{}:\n{}\n\n{}:\n{}".format(
            latest["tag_name"],
            latest["_changelog"],
            testing["tag_name"],
            testing["_changelog"]
            ),
        "AssemblyVersion": latest["tag_name"][1:],
        "TestingAssemblyVersion": testing["tag_name"][1:],
        "DownloadLinkInstall": latest["assets"][0]["browser_download_url"],
        "DownloadLinkUpdate": latest["assets"][0]["browser_download_url"],
        "DownloadLinkTesting": testing["assets"][0]["browser_download_url"],
        "DalamudApiLevel": latest["_dalamud"],
        "TestingDalamudApiLevel": testing["_dalamud"]
        })
    with open(f"{ROOT}/pluginmaster.json","w") as f:
        dump(pluginmaster,f,indent=2)

if __name__ == "__main__":
    main()
