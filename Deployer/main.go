// Mercury Setup Deployer 4
// The only setup deployer that isn't overengineered

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
	input  = "./staging"
	output = "./setup"
)

func compressStagingDir(o *bytes.Buffer) (err error) {
	gz, _ := gzip.NewWriterLevel(o, gzip.BestCompression)
	defer gz.Close()

	w := tar.NewWriter(gz)
	defer w.Close()

	return w.AddFS(os.DirFS(input))
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

	// create output directory if it doesn't exist
	if _, err := os.Stat(output); os.IsNotExist(err) {
		if err = os.Mkdir(output, 0o755); err != nil {
			fmt.Println("Error creating output directory:", err)
			os.Exit(1)
		}
	}

	fmt.Println("Output directory is ready.")

	start := time.Now()

	o := &bytes.Buffer{}
	if err := compressStagingDir(o); err != nil {
		fmt.Println("Error compressing staging directory:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging directory compressed in %s\n", time.Since(start))

	// Upload compressed data to IPFS
	file := files.NewReaderFile(o)

	// get local IPFS node API
	api, err := rpc.NewLocalApi()
	if err != nil {
		fmt.Println("Error connecting to local IPFS node:", err)
		os.Exit(1)
	}

	cidFile, err := api.Unixfs().Add(context.Background(), file)
	if err != nil {
		fmt.Println("Error adding file to IPFS via local node:", err)
		os.Exit(1)
	}

	cid := cidFile.RootCid().String()
	fmt.Println("Generated IPFS CID via local node:", cid)

	// pin CID
	if err := api.Pin().Add(context.Background(), cidFile); err != nil {
		fmt.Println("Error pinning file on local IPFS node:", err)
		os.Exit(1)
	}

	// create or modify version.txt in output directory
	versionFile, err := os.Create(output + "/version")
	if err != nil {
		fmt.Println("Error creating version file:", err)
		os.Exit(1)
	}
	defer versionFile.Close()

	if _, err = versionFile.WriteString(cid); err != nil {
		fmt.Println("Error writing to version file:", err)
		os.Exit(1)
	}

	fmt.Println("version file created with ID", cid)
	fmt.Println("Setup deployer completed successfully.")
}
