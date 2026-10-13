package config

import (
	"strings"
	"testing"
)

func env(values map[string]string) func(string) string {
	return func(key string) string { return values[key] }
}

func validEnv() map[string]string {
	return map[string]string{
		"Blob__Endpoint":        "http://minio:9000",
		"Blob__AccessKeyId":     "id",
		"Blob__SecretAccessKey": "secret",
	}
}

func TestDefaults(t *testing.T) {
	cfg, err := Load(env(validEnv()))
	if err != nil {
		t.Fatal(err)
	}
	if cfg.HTTPPort != "8080" || cfg.GRPCPort != "50051" || cfg.Blob.Bucket != DefaultBucket ||
		cfg.ScheduleHour != 1 || cfg.MaxRangeDays != 31 || strings.Join(cfg.ScheduledProviders, ",") != "BOE" {
		t.Fatalf("unexpected defaults: %+v", cfg)
	}
}

func TestListsAreTrimmed(t *testing.T) {
	values := validEnv()
	values["BOJA_FEEDS"] = " https://a/s51.xml, ,https://a/s52.xml "
	values["SCRAPE_SCHEDULED_PROVIDERS"] = "BOE,BOJA"

	cfg, err := Load(env(values))
	if err != nil {
		t.Fatal(err)
	}
	if len(cfg.BojaFeeds) != 2 || cfg.BojaFeeds[1] != "https://a/s52.xml" || len(cfg.ScheduledProviders) != 2 {
		t.Fatalf("unexpected lists: %+v", cfg)
	}
}

func TestAllProblemsAreReportedTogether(t *testing.T) {
	_, err := Load(env(map[string]string{
		"PORT":                 "http",
		"SCRAPE_SCHEDULE_HOUR": "25",
		"DOTNET_API_GRPC_URL":  "apiservice:50051",
		"LOG_LEVEL":            "verbose",
	}))
	if err == nil {
		t.Fatal("expected errors")
	}
	for _, want := range []string{"PORT", "SCRAPE_SCHEDULE_HOUR", "DOTNET_API_GRPC_URL", "LOG_LEVEL", "Blob__Endpoint"} {
		if !strings.Contains(err.Error(), want) {
			t.Errorf("error does not mention %s: %v", want, err)
		}
	}
}

func TestBlobCredentialsAreRequiredWithAnEndpoint(t *testing.T) {
	values := validEnv()
	delete(values, "Blob__SecretAccessKey")

	if _, err := Load(env(values)); err == nil || !strings.Contains(err.Error(), "Blob__SecretAccessKey") {
		t.Fatalf("err = %v", err)
	}
}
