// Mercury Setup Deployer 4
// The only setup deployer that isn't overengineered (this aged questionably)

package main

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"context"
	"fmt"
	"os"
	"time"

	"github.com/ipfs/boxo/files"
	"github.com/ipfs/kubo/client/rpc"
)

const (
	staging  = "./staging"
	versions = "./versions.txt"
)

var launchers = map[string]string{
	"MercuryLauncher_win-x64.exe": "./launchers/MercuryLauncher.exe",
	"MercuryLauncher_linux-x64":   "./launchers/MercuryLauncher",
}

func compressStagingDir(o *bytes.Buffer) (err error) {
	gz, _ := gzip.NewWriterLevel(o, gzip.BestCompression)
	defer gz.Close()

	w := tar.NewWriter(gz)
	defer w.Close()

	return w.AddFS(os.DirFS(staging))
}

func uploadAndPin(api *rpc.HttpApi, file files.File) (string, error) {
	defer file.Close()

	ctx := context.Background()

	cidFile, err := api.Unixfs().Add(ctx, file)
	if err != nil {
		return "", fmt.Errorf("add file to local IPFS node: %w", err)
	}

	cid := cidFile.RootCid().String()
	fmt.Println(cid, "added to IPFS")

	// announce provision of files (this takes a while!)
	// if err := api.Routing().Provide(ctx, cidFile); err != nil {
	// 	return "", fmt.Errorf("provide file on network: %w", err)
	// }
	// fmt.Println(cid, "provided on network")

	// pin CID
	if err := api.Pin().Add(ctx, cidFile); err != nil {
		return "", fmt.Errorf("pin file on local IPFS node: %w", err)
	}
	fmt.Println(cid, "pinned on local node")

	return cid, nil
}

func main() {
	fmt.Println("MERCURY SETUP DEPLOYER 4")

	stagingFiles, err := os.ReadDir("staging")
	if err != nil {
		fmt.Println("Error reading staging directory:", err)
		fmt.Println("Please create the staging directory if it doesn't exist and place your files in it, or run this script from a different directory.")
		os.Exit(1)
	}
	if len(stagingFiles) == 0 {
		fmt.Println("Staging directory is empty. Please place your files in the staging directory, or run this script from a different directory.")
		os.Exit(1)
	}

	fmt.Println("Staging directory contains files.")

	// check if each launcher exists
	for name, path := range launchers {
		if _, err := os.Stat(path); os.IsNotExist(err) {
			fmt.Printf("Launcher for %s not found at %s. Please ensure all launchers are present.\n", name, path)
			os.Exit(1)
		}
	}

	fmt.Println("All launchers are present.")

	start := time.Now()

	o := &bytes.Buffer{}
	if err := compressStagingDir(o); err != nil {
		fmt.Println("Error compressing staging directory:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging directory compressed in %s\n", time.Since(start))

	// Create versions file
	versionsFile, err := os.Create(versions)
	if err != nil {
		fmt.Println("Error creating versions file:", err)
		os.Exit(1)
	}
	defer versionsFile.Close()

	// get local IPFS node API
	api, err := rpc.NewLocalApi()
	if err != nil {
		fmt.Println("Error connecting to local IPFS node:", err)
		os.Exit(1)
	}

	// Upload compressed setup to IPFS
	cid, err := uploadAndPin(api, files.NewReaderFile(o))
	if err != nil {
		fmt.Println("Error uploading and pinning setup:", err)
		os.Exit(1)
	}
	fmt.Fprintf(versionsFile, "%s setup\n", cid)

	// Upload launchers to IPFS
	for name, path := range launchers {
		launcherFile, err := os.Open(path)
		if err != nil {
			fmt.Printf("Error opening launcher file for %s: %v\n", name, err)
			os.Exit(1)
		}
		defer launcherFile.Close()

		lcid, err := uploadAndPin(api, files.NewReaderFile(launcherFile))
		if err != nil {
			fmt.Printf("Error uploading and pinning launcher for %s: %v\n", name, err)
			os.Exit(1)
		}
		fmt.Fprintf(versionsFile, "%s %s\n", lcid, name)
	}

	fmt.Println("Setup deployer completed successfully.")
}
