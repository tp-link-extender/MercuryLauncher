// Mercury Setup Deployer 4
// The only setup deployer that isn't overengineered

package main

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"crypto/sha3"
	"encoding/base32"
	"fmt"
	"io"
	"os"
	"time"
)

const (
	input  = "./staging"
	output = "./setup"
)

var encoding = base32.NewEncoding("0123456789abcdefghijklmnopqrstuv").WithPadding(base32.NoPadding)

func compressStagingDir(o *bytes.Buffer) (id string, err error) {
	gz, _ := gzip.NewWriterLevel(o, gzip.BestCompression)
	defer gz.Close()

	w := tar.NewWriter(gz)
	defer w.Close()

	if err = w.AddFS(os.DirFS(input)); err != nil {
		return
	}

	hash := sha3.SumSHAKE256(o.Bytes(), 8)
	enchash := encoding.EncodeToString(hash[:])

	return enchash, nil
}

func writeStagingDir(hash string, o *bytes.Buffer) (err error) {
	// write to output file
	outputFile, err := os.Create(output + "/" + hash)
	if err != nil {
		return fmt.Errorf("error creating output file: %w", err)
	}
	defer outputFile.Close()

	if _, err = io.Copy(outputFile, o); err != nil {
		return fmt.Errorf("error writing to output file: %w", err)
	}

	return
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
	id, err := compressStagingDir(o)
	if err != nil {
		fmt.Println("Error compressing staging directory:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging directory compressed in %s\n", time.Since(start))
	start = time.Now()

	// zip staging files to output directory
	if err := writeStagingDir(id, o); err != nil {
		fmt.Println("Error compressing staging files:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging files written to output directory in %s\n", time.Since(start))

	// create or modify version.txt in output directory
	versionFile, err := os.Create(output + "/version")
	if err != nil {
		fmt.Println("Error creating version file:", err)
		os.Exit(1)
	}
	defer versionFile.Close()

	if _, err = versionFile.WriteString(id); err != nil {
		fmt.Println("Error writing to version file:", err)
		os.Exit(1)
	}

	fmt.Println("version file created with ID", id)
	fmt.Println("Setup deployer completed successfully.")
}
