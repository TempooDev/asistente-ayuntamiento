export interface UserProfileDto {
  fullName: string;
  department: string;
  position: string;
  phoneNumber: string;
}

export interface UserPreferenceDto {
  topics: string[];
  locations: string[];
}
