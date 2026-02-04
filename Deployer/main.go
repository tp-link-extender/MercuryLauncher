// Mercury Setup Deployer 4
// The only setup deployer that isn't overengineered (again!)

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
	staging = "./staging"
	output  = "./setup"
	name    = "Mercury"
)

var launchers = map[string]string{
	name + "Launcher_win-x64.exe": "./launchers/" + name + "Launcher.exe",
	name + "Launcher_linux-x64":   "./launchers/" + name + "Launcher",
}

var encoding = base32.NewEncoding("0123456789abcdefghijklmnopqrstuv").WithPadding(base32.NoPadding)

func compressStagingDir(o *bytes.Buffer) (id string, err error) {
	gz, _ := gzip.NewWriterLevel(o, gzip.BestCompression)
	defer gz.Close()

	w := tar.NewWriter(gz)
	defer w.Close()

	if err = w.AddFS(os.DirFS(staging)); err != nil {
		return
	}

	hash := sha3.SumSHAKE256(o.Bytes(), 8)
	enchash := encoding.EncodeToString(hash[:])

	return enchash, nil
}

type fileHash struct {
	name, enchash string
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

	// check if each launcher exists
	for name, path := range launchers {
		if _, err := os.Stat(path); os.IsNotExist(err) {
			fmt.Printf("Launcher for %s not found at %s. Please ensure all launchers are present.\n", name, path)
			os.Exit(1)
		}
	}

	fmt.Println("All launchers are present.")

	// create output directory if it doesn't exist
	if _, err := os.Stat(output); os.IsNotExist(err) {
		if err = os.Mkdir(output, 0o755); err != nil {
			fmt.Println("Error creating output directory:", err)
			os.Exit(1)
		}
	}

	fmt.Println("Output directory is ready.")

	fmt.Println("Compressing staging directory...")
	start := time.Now()

	o := &bytes.Buffer{}
	id, err := compressStagingDir(o)
	if err != nil {
		fmt.Println("Error compressing staging directory:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging directory compressed in %s\n", time.Since(start))

	// gzip staging files to output directory
	start = time.Now()

	if err := writeStagingDir(id, o); err != nil {
		fmt.Println("Error compressing staging files:", err)
		os.Exit(1)
	}

	fmt.Printf("Staging files written to output directory in %s\n", time.Since(start))

	fileHashes := []fileHash{
		{name: "staging", enchash: id},
	}

	for name, path := range launchers {
		launcherData, err := os.ReadFile(path)
		if err != nil {
			fmt.Printf("Error reading launcher %s: %v\n", name, err)
			os.Exit(1)
		}

		// copy launcher to output directory
		outputLauncherFile, err := os.Create(output + "/" + name)
		if err != nil {
			fmt.Printf("Error creating output launcher file %s: %v\n", name, err)
			os.Exit(1)
		}
		if _, err := outputLauncherFile.Write(launcherData); err != nil {
			fmt.Printf("Error writing to output launcher file %s: %v\n", name, err)
			os.Exit(1)
		}

		launcherHash := sha3.SumSHAKE256(launcherData, 8)
		enchash := encoding.EncodeToString(launcherHash[:])

		fileHashes = append(fileHashes, fileHash{name: name, enchash: enchash})
	}

	// create or modify version.txt in output directory
	versionFile, err := os.Create(output + "/version")
	if err != nil {
		fmt.Println("Error creating version file:", err)
		os.Exit(1)
	}
	defer versionFile.Close()

	for _, file := range fileHashes {
		if _, err := fmt.Fprintf(versionFile, "%s %s\n", file.enchash, file.name); err != nil {
			fmt.Println("Error writing to version file:", err)
			os.Exit(1)
		}
	}

	fmt.Println("version file created with ID", id)
	fmt.Println("Setup deployer completed successfully.")
}
