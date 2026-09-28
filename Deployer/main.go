// Mercury Setup Deployer 5
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
	output   = "./setup"
	name     = "Mercury"
	staging  = "./staging"
	stagingD = staging + "/%s"
)

var launchers = map[string]string{
	name + "Launcher_win-x64.exe": "./launchers/" + name + "Launcher.exe",
	name + "Launcher_linux-x64":   "./launchers/" + name + "Launcher",
}

var encoding = base32.NewEncoding("0123456789abcdefghijklmnopqrstuv").WithPadding(base32.NoPadding)

func compressStagingDir(o *bytes.Buffer, staging string) (id string, err error) {
	gz, _ := gzip.NewWriterLevel(o, gzip.BestCompression)

	w := tar.NewWriter(gz)

	if err = w.AddFS(os.DirFS(staging)); err != nil {
		return
	}

	// close the writers before hashing so the buffer holds the complete
	// archive and each year's content hashes uniquely
	if err = w.Close(); err != nil {
		return
	}

	if err = gz.Close(); err != nil {
		return
	}

	hash := sha3.SumSHAKE256(o.Bytes(), 8)
	enchash := encoding.EncodeToString(hash[:])

	return enchash, nil
}

func writeStagingDir(hash string, o *bytes.Buffer, output string) (err error) {
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

// compress a single year's staging directory and write it to that year's
// output directory, e.g. staging/2013 -> setup/2013/{hash}
func deployYear(stagingDir, outputDir string) error {
	stagingFiles, err := os.ReadDir(stagingDir)
	if err != nil {
		return fmt.Errorf("error reading staging directory %s: %w", stagingDir, err)
	}
	if len(stagingFiles) == 0 {
		return fmt.Errorf("staging directory %s is empty. Please place your files in it", stagingDir)
	}

	// create output directory if it doesn't exist
	if _, err := os.Stat(outputDir); os.IsNotExist(err) {
		if err = os.Mkdir(outputDir, 0o755); err != nil {
			return fmt.Errorf("error creating output directory: %w", err)
		}
	}

	fmt.Printf("Deploying %s -> %s\n", stagingDir, outputDir)

	start := time.Now()

	o := &bytes.Buffer{}
	id, err := compressStagingDir(o, stagingDir)
	if err != nil {
		return fmt.Errorf("error compressing staging directory: %w", err)
	}

	fmt.Printf("Staging directory compressed in %s\n", time.Since(start))

	start = time.Now()

	if err := writeStagingDir(id, o, outputDir); err != nil {
		return fmt.Errorf("error compressing staging files: %w", err)
	}

	fmt.Printf("Staging files written to output directory in %s\n", time.Since(start))

	// create or overwrite the version file in the output directory
	versionFile, err := os.Create(outputDir + "/version")
	if err != nil {
		return fmt.Errorf("error creating version file: %w", err)
	}
	defer versionFile.Close()

	if _, err := versionFile.WriteString(id); err != nil {
		return fmt.Errorf("error writing to version file: %w", err)
	}

	fmt.Println("Version file created with ID", id)

	return nil
}

func main() {
	fmt.Println("MERCURY SETUP DEPLOYER 5")

	if err := os.Mkdir(output, 0o755); err != nil && !os.IsExist(err) {
		fmt.Println("Error creating output directory:", err)
		os.Exit(1)
	}

	// a single launcher build serves both client years, so it is copied once
	// to the setup root rather than into each year's directory
	for launcherName, path := range launchers {
		if _, err := os.Stat(path); os.IsNotExist(err) {
			fmt.Printf("Launcher %s not found at %s. Please ensure all launchers are present.\n", launcherName, path)
			os.Exit(1)
		}
	}

	for launcherName, path := range launchers {
		launcherData, err := os.ReadFile(path)
		if err != nil {
			fmt.Printf("Error reading launcher %s: %v\n", launcherName, err)
			os.Exit(1)
		}

		// copy launcher to output directory
		outputLauncherFile, err := os.Create(output + "/" + launcherName)
		if err != nil {
			fmt.Printf("Error creating output launcher file %s: %v\n", launcherName, err)
			os.Exit(1)
		}
		if _, err := outputLauncherFile.Write(launcherData); err != nil {
			fmt.Printf("Error writing to output launcher file %s: %v\n", launcherName, err)
			os.Exit(1)
		}
		if err := outputLauncherFile.Close(); err != nil {
			fmt.Printf("Error closing output launcher file %s: %v\n", launcherName, err)
			os.Exit(1)
		}

		fmt.Printf("Launcher %s copied to output directory.\n", launcherName)
	}

	// each year gets its own staging directory and output directory, so the
	// Launcher can fetch per-year clients from setup.<domain>/<year>/...
	for _, year := range []string{"2013", "2016"} {
		if err := deployYear(fmt.Sprintf(stagingD, year), output+"/"+year); err != nil {
			fmt.Printf("Error deploying %s: %v\n", year, err)
			fmt.Println("Skipping to next year...")
			continue
		}
	}

	fmt.Println("Setup deployer completed successfully.")
}
